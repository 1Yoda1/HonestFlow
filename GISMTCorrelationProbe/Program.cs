using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GISMTCorrelationProbe;

internal static class Program
{
    private const string LogDirectory = @"C:\ProgramData\ESP\ESM\um\log";
    private static readonly CancellationTokenSource Stop = new();

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test") return await ProbeSelfTests.RunAsync();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintHeader();
        var outputPath = CreateOutputPath();
        await using var writer = new NdjsonWriter(outputPath);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var esm = new EsmApi(client);
        var correlationTasks = new ConcurrentBag<Task>();
        Task? samplerTask = null;
        try
        {
            await esm.InitializeAsync(Stop.Token);
            Console.WriteLine($"\nESM API:\n127.0.0.1:{esm.Port}\n\nInstance:\n{esm.InstanceId}\n\nOutput:\n{outputPath}");
            var samples = new GismtSampleBuffer(TimeSpan.FromSeconds(10));
            var sampler = new GismtSampler(esm, samples);
            await sampler.SampleOnceAsync(Stop.Token);
            var baseline = samples.Nearest(DateTimeOffset.Now) ?? GismtSample.Empty(DateTimeOffset.Now, "baseline недоступен");
            await writer.WriteAsync(new Snapshot(CdnTransaction.Baseline(), "BASELINE", esm.Port, esm.InstanceId, baseline), Stop.Token);
            samplerTask = sampler.RunAsync(Stop.Token);
            Console.WriteLine("\nWaiting for CDN activity...");
            var parser = new EsmHttpBlockParser();
            var deduplicator = new EventDeduplicator();
            var tailer = new LogTailer(LogDirectory);
            await foreach (var logLine in tailer.ReadLinesAsync(Stop.Token))
                foreach (var transaction in parser.Process(logLine))
                    if (deduplicator.TryAccept(transaction)) correlationTasks.Add(CorrelateTransactionAsync(transaction, samples, sampler, esm, writer, Stop.Token));
            await samplerTask;
        }
        catch (OperationCanceledException) when (Stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nОшибка запуска: {SafeError(ex)}");
            Console.Error.WriteLine("Нажмите любую клавишу для завершения."); Console.ReadKey(intercept: true); return 1;
        }
        finally
        {
            if (samplerTask != null) try { await samplerTask; } catch (OperationCanceledException) { }
            try { await Task.WhenAll(correlationTasks.ToArray()); } catch (OperationCanceledException) { }
            await writer.FlushAsync(); Console.WriteLine($"\nNDJSON сохранён:\n{outputPath}");
        }
        return 0;
    }

    private static async Task CorrelateTransactionAsync(CdnTransaction tx, GismtSampleBuffer samples, GismtSampler sampler, EsmApi esm, NdjsonWriter writer, CancellationToken token)
    {
        if (tx.IsFailure)
        {
            Console.WriteLine($"\n[{tx.BlockTime:HH:mm:ss.fff}] CDN FAILURE\n{tx.Method} {tx.Host} {tx.Endpoint}\n{tx.TransportError}");
            await WritePhaseAsync(tx, "FAILURE", tx.BlockTime, samples, esm, writer, token);
            await sampler.WaitUntilAsync(tx.BlockTime.AddMilliseconds(500), token);
            await WritePhaseAsync(tx, "AFTER_500MS", tx.BlockTime.AddMilliseconds(500), samples, esm, writer, token);
            return;
        }
        var estimated = tx.EstimatedRequestTime!.Value;
        Console.WriteLine($"\n[{tx.BlockTime:HH:mm:ss.fff}] CDN TRANSACTION\n{tx.Method} {tx.Host} {tx.Endpoint}\nHTTP={tx.HttpStatus}\ntime={tx.ResponseTimeMs?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms\n\nEstimated request:\n{estimated:HH:mm:ss.fff}");
        await WritePhaseAsync(tx, "REQUEST", estimated, samples, esm, writer, token);
        await WritePhaseAsync(tx, "RESPONSE", tx.BlockTime, samples, esm, writer, token);
        await sampler.WaitUntilAsync(tx.BlockTime.AddMilliseconds(500), token);
        await WritePhaseAsync(tx, "AFTER_500MS", tx.BlockTime.AddMilliseconds(500), samples, esm, writer, token);
    }

    private static async Task WritePhaseAsync(CdnTransaction tx, string phase, DateTimeOffset target, GismtSampleBuffer samples, EsmApi esm, NdjsonWriter writer, CancellationToken token)
    {
        var sample = samples.Nearest(target) ?? GismtSample.Empty(target, "Нет GISMT sample для точки корреляции");
        await writer.WriteAsync(new Snapshot(tx, phase, esm.Port, esm.InstanceId, sample), token);
        Console.WriteLine($"\n{phase.Replace("_", " ")}\ngismt.code={Display(sample.Status.Code)}\nerror={Display(sample.Status.Error)}\nlastConnection={Display(sample.Status.LastConnection)}");
    }

    private static string Display(object? value) => value?.ToString() ?? "-";
    private static void PrintHeader()
    {
        var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Console.WriteLine("GIS MT CORRELATION PROBE\n\nЗапущено с правами администратора: " + (isAdmin ? "Да" : "Нет"));
        Console.WriteLine($"\nESM log directory:\n{LogDirectory}");
    }
    private static string CreateOutputPath()
    {
        var stem = "gis-correlation_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        for (var i = 0; ; i++) { var path = Path.Combine(AppContext.BaseDirectory, stem + (i == 0 ? "" : $"_{i:D2}") + ".ndjson"); if (!File.Exists(path)) return path; }
    }
    private static string SafeError(Exception ex) => ex is HttpRequestException ? "локальный ESM API недоступен" : ex is TaskCanceledException ? "превышено время ожидания локального ESM API" : ex.GetType().Name;
    internal sealed record GismtStatus(object? Code, object? Error, object? LastConnection, object? Name, object? Version, object? Id, int? HttpStatus, string? RequestError);
}

internal sealed class NdjsonWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer; private readonly SemaphoreSlim _gate = new(1, 1);
    public NdjsonWriter(string path) => _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
    public async Task WriteAsync<T>(T record, CancellationToken token) { var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }); await _gate.WaitAsync(token); try { await _writer.WriteLineAsync(json); await _writer.FlushAsync(); } finally { _gate.Release(); } }
    public Task FlushAsync() => _writer.FlushAsync(); public async ValueTask DisposeAsync() { await _writer.DisposeAsync(); _gate.Dispose(); }
}

internal sealed record GismtSample(DateTimeOffset Timestamp, Program.GismtStatus Status)
{
    public static GismtSample Empty(DateTimeOffset time, string error) => new(time, new Program.GismtStatus(null, null, null, null, null, null, null, error));
}

internal sealed class GismtSampleBuffer
{
    private readonly object _sync = new(); private readonly Queue<GismtSample> _samples = new(); private readonly TimeSpan _retention;
    public GismtSampleBuffer(TimeSpan retention) => _retention = retention;
    public void Add(GismtSample sample) { lock (_sync) { _samples.Enqueue(sample); while (_samples.Count > 0 && sample.Timestamp - _samples.Peek().Timestamp > _retention) _samples.Dequeue(); } }
    public GismtSample? Nearest(DateTimeOffset target) { lock (_sync) return _samples.OrderBy(x => Math.Abs((x.Timestamp - target).TotalMilliseconds)).FirstOrDefault(); }
}

internal sealed class GismtSampler
{
    private readonly EsmApi _api; private readonly GismtSampleBuffer _buffer;
    public GismtSampler(EsmApi api, GismtSampleBuffer buffer) { _api = api; _buffer = buffer; }
    public async Task RunAsync(CancellationToken token) { while (!token.IsCancellationRequested) { await SampleOnceAsync(token); await Task.Delay(50, token); } }
    public async Task SampleOnceAsync(CancellationToken token) { var status = await _api.GetGismtStatusAsync(token); _buffer.Add(new GismtSample(DateTimeOffset.Now, status)); }
    public async Task WaitUntilAsync(DateTimeOffset time, CancellationToken token) { var delay = time - DateTimeOffset.Now; if (delay > TimeSpan.Zero) await Task.Delay(delay, token); await Task.Delay(75, token); }
}

internal sealed record CdnTransaction(string EventId, DateTimeOffset BlockTime, string? Method, string? Host, string? Endpoint, int? HttpStatus, double? ResponseTimeMs, int? ProtocolCode, string? Description, string? TransportError)
{
    public bool IsFailure => TransportError != null;
    public DateTimeOffset? EstimatedRequestTime => ResponseTimeMs is double ms ? BlockTime - TimeSpan.FromMilliseconds(ms) : null;
    public static CdnTransaction Baseline() => new("baseline", DateTimeOffset.Now, null, null, null, null, null, null, null, null);
}

internal sealed record Snapshot
{
    public Snapshot(CdnTransaction tx, string phaseValue, int port, string? instance, GismtSample sample)
    {
        eventId = tx.EventId; cdnRequestTime = tx.EstimatedRequestTime; snapshotTime = sample.Timestamp; phase = phaseValue; correlationMode = tx.IsFailure ? "transport_failure_log_timestamp" : tx.Method is null ? "baseline" : "reconstructed_from_log_timespan";
        cdnHost = tx.Host; method = tx.Method; endpoint = tx.Endpoint; cdnHttpStatus = tx.HttpStatus; cdnProtocolCode = tx.ProtocolCode; cdnDescription = tx.Description; cdnResponseTimeMs = tx.ResponseTimeMs; cdnTransportError = tx.TransportError;
        esmPort = port; instanceId = instance; gismtCode = sample.Status.Code; gismtError = sample.Status.Error; gismtLastConnection = sample.Status.LastConnection; gismtName = sample.Status.Name; gismtVersion = sample.Status.Version; gismtId = sample.Status.Id; esmStatusHttpStatus = sample.Status.HttpStatus; esmStatusError = sample.Status.RequestError;
    }
    public string eventId { get; } public DateTimeOffset? cdnRequestTime { get; } public DateTimeOffset snapshotTime { get; } public string phase { get; } public string correlationMode { get; }
    public string? cdnHost { get; } public string? method { get; } public string? endpoint { get; } public int? cdnHttpStatus { get; } public int? cdnProtocolCode { get; } public string? cdnDescription { get; } public double? cdnResponseTimeMs { get; } public string? cdnTransportError { get; }
    public int esmPort { get; } public string? instanceId { get; } public object? gismtCode { get; } public object? gismtError { get; } public object? gismtLastConnection { get; } public object? gismtName { get; } public object? gismtVersion { get; } public object? gismtId { get; } public int? esmStatusHttpStatus { get; } public string? esmStatusError { get; }
}

internal sealed record LogLine(string SourcePath, string Text);

internal sealed class EsmHttpBlockParser
{
    private static readonly Regex TimestampRegex = new(@"(?<date>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})", RegexOptions.Compiled);
    private static readonly Regex MethodRegex = new(@"^\s*Method:\s*(?<method>POST|GET)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlRegex = new(@"^\s*URL:\s*https://(?<host>cdn[^./\s]*\.crpt\.ru):19101(?<endpoint>/api/v4/(?:cdn/codes/check|auth|cdn/getconf|cdn/health/check))(?:[/?#\s].*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ResponseTimeRegex = new(@"Время ответа:\s*(?<ms>\d+(?:[.,]\d+)?)\s*ms", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StatusRegex = new(@"^\s*Status:\s*(?<status>[1-5]\d\d)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TopNumberRegex = new("^\\s*\\\"(?<name>code|status)\\\"\\s*:\\s*(?<value>-?\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DescriptionRegex = new("^\\s*\\\"description\\\"\\s*:\\s*\\\"(?<value>[^\\\"\\r\\n]{0,160})\\\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UrlAnywhereRegex = new(@"https://(?<host>cdn[^./\s]*\.crpt\.ru):19101(?<endpoint>/api/v4/(?:cdn/codes/check|auth|cdn/getconf|cdn/health/check))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly Dictionary<string, BlockState> _states = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<CdnTransaction> Process(LogLine line)
    {
        var parsedTimestamp = ParseTimestamp(line.Text);
        var timestamp = parsedTimestamp ?? DateTimeOffset.Now;
        if (TryTransportFailure(line.Text, timestamp, out var failure)) { yield return failure; yield break; }

        if (!_states.TryGetValue(line.SourcePath, out var state)) _states[line.SourcePath] = state = new BlockState();
        if (parsedTimestamp.HasValue) state.LastTimestamp = parsedTimestamp.Value;
        if (line.Text.Contains("HTTP Request", StringComparison.OrdinalIgnoreCase)) { state.Reset(state.LastTimestamp ?? timestamp); yield break; }
        if (!state.InBlock) yield break;

        var method = MethodRegex.Match(line.Text);
        if (method.Success) state.Method = method.Groups["method"].Value.ToUpperInvariant();
        var url = UrlRegex.Match(line.Text);
        if (url.Success) { state.Host = url.Groups["host"].Value.ToLowerInvariant(); state.Endpoint = url.Groups["endpoint"].Value; }
        var responseTime = ResponseTimeRegex.Match(line.Text);
        if (responseTime.Success && double.TryParse(responseTime.Groups["ms"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var milliseconds)) state.ResponseTimeMs = milliseconds;
        if (line.Text.Contains("HTTP Response", StringComparison.OrdinalIgnoreCase)) { state.InResponse = true; state.InResponseBody = false; }
        var status = StatusRegex.Match(line.Text);
        if (state.InResponse && status.Success) state.HttpStatus = int.Parse(status.Groups["status"].Value, CultureInfo.InvariantCulture);
        if (state.InResponse && line.Text.TrimStart().StartsWith("Body:", StringComparison.OrdinalIgnoreCase)) state.InResponseBody = true;
        if (state.InResponseBody) state.ReadSafeResponseMetadata(line.Text, TopNumberRegex, DescriptionRegex);

        if (line.Text.Contains("==================================", StringComparison.Ordinal) && state.IsComplete)
        {
            var tx = state.ToTransaction(); state.Clear(); yield return tx;
        }
    }

    private static DateTimeOffset? ParseTimestamp(string text)
    {
        var match = TimestampRegex.Match(text);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["date"].Value, "yyyy.MM.dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return null;
        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }
    private static bool TryTransportFailure(string text, DateTimeOffset time, out CdnTransaction transaction)
    {
        transaction = default!;
        var url = UrlAnywhereRegex.Match(text);
        if (!url.Success || !Regex.IsMatch(text, @"(?i)(ошибка выполнения|deadline exceeded|timeout|timed out|connection.*(?:refused|reset|failed)|connection refused)")) return false;
        var lower = text.ToLowerInvariant();
        var category = lower.Contains("deadline exceeded") || lower.Contains("timeout") || lower.Contains("timed out") ? "timeout" : lower.Contains("connection") ? "connection_error" : "unknown_transport_error";
        var method = Regex.IsMatch(text, @"(?i)\bpost\b") ? "POST" : Regex.IsMatch(text, @"(?i)\bget\b") ? "GET" : null;
        transaction = new CdnTransaction(Guid.NewGuid().ToString("N"), time, method, url.Groups["host"].Value.ToLowerInvariant(), url.Groups["endpoint"].Value, null, null, null, null, category);
        return true;
    }
    private sealed class BlockState
    {
        public bool InBlock; public bool InResponse; public bool InResponseBody; public int JsonDepth; public DateTimeOffset Timestamp; public DateTimeOffset? LastTimestamp; public string? Method; public string? Host; public string? Endpoint; public int? HttpStatus; public double? ResponseTimeMs; public int? ProtocolCode; public string? Description;
        public bool IsComplete => Method != null && Host != null && Endpoint != null && HttpStatus != null && ResponseTimeMs != null;
        public void Reset(DateTimeOffset timestamp) { Clear(); InBlock = true; Timestamp = timestamp; }
        public void Clear() { InBlock = false; InResponse = false; InResponseBody = false; JsonDepth = 0; Method = Host = Endpoint = Description = null; HttpStatus = ProtocolCode = null; ResponseTimeMs = null; }
        public void ReadSafeResponseMetadata(string line, Regex number, Regex description)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("{")) JsonDepth++;
            if (JsonDepth == 1)
            {
                var numeric = number.Match(line);
                if (numeric.Success && int.TryParse(numeric.Groups["value"].Value, out var value)) ProtocolCode = value;
                var text = description.Match(line);
                if (text.Success) Description = SafeDescription(text.Groups["value"].Value);
            }
            foreach (var ch in line) if (ch == '}') JsonDepth--;
        }
        private static string? SafeDescription(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length is 0 or > 160 || Regex.IsMatch(trimmed, @"(?i)(token|certificate|x-fn-sid|x-crc32|x-reqid|x-trnid|\bfn\b|\bcis\b|\bmark\b|\bcodes\b|reqid|credential)")) return null;
            return trimmed.Equals("ok", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("success", StringComparison.OrdinalIgnoreCase) || (trimmed.Contains(' ') && Regex.IsMatch(trimmed, @"^[\p{L}\p{N}\s.,:;!?()\-]+$")) ? trimmed : null;
        }
        public CdnTransaction ToTransaction() => new(Guid.NewGuid().ToString("N"), Timestamp, Method, Host, Endpoint, HttpStatus, ResponseTimeMs, ProtocolCode, Description, null);
    }
}

internal sealed class EventDeduplicator
{
    private readonly object _sync = new(); private readonly Dictionary<string, DateTimeOffset> _seen = new();
    public bool TryAccept(CdnTransaction tx)
    {
        var key = $"{tx.BlockTime:O}|{tx.Method}|{tx.Host}|{tx.Endpoint}|{tx.HttpStatus}|{tx.ResponseTimeMs}|{tx.TransportError}";
        lock (_sync) { foreach (var old in _seen.Where(x => tx.BlockTime - x.Value > TimeSpan.FromMinutes(2)).Select(x => x.Key).ToList()) _seen.Remove(old); if (_seen.ContainsKey(key)) return false; _seen[key] = tx.BlockTime; return true; }
    }
}

internal sealed class EsmApi
{
    private readonly HttpClient _client;
    public EsmApi(HttpClient client) => _client = client;
    public int Port { get; private set; } public string? InstanceId { get; private set; }
    public async Task InitializeAsync(CancellationToken token) { Port = await ReadPortAsync(token); await RefreshInstanceAsync(token); }
    public async Task<Program.GismtStatus> GetGismtStatusAsync(CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(InstanceId)) await RefreshInstanceAsync(token);
            using var response = await _client.GetAsync($"http://127.0.0.1:{Port}/api/v1/status/{Uri.EscapeDataString(InstanceId!)}", token);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                await RefreshInstanceAsync(token);
                using var retry = await _client.GetAsync($"http://127.0.0.1:{Port}/api/v1/status/{Uri.EscapeDataString(InstanceId!)}", token);
                return await ParseStatusAsync(retry, token);
            }
            return await ParseStatusAsync(response, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        { return new Program.GismtStatus(null, null, null, null, null, null, null, SafeError(ex)); }
    }
    private async Task RefreshInstanceAsync(CancellationToken token)
    {
        using var response = await _client.GetAsync($"http://127.0.0.1:{Port}/api/v1/instances/info", token); response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var array = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : GetProperty(document.RootElement, "instances");
        if (array.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("ESM API не вернул список instances.");
        foreach (var item in array.EnumerateArray())
        {
            var id = GetProperty(item, "id");
            if (id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())) { InstanceId = id.GetString(); return; }
            if (id.ValueKind == JsonValueKind.Number) { InstanceId = id.GetRawText(); return; }
        }
        throw new InvalidOperationException("В ESM API не найден instance id.");
    }
    private static async Task<int> ReadPortAsync(CancellationToken token)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(@"C:\ProgramData\ESP\ESM\esm-gui\gui_settings.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                foreach (var name in new[] { "localApiPort", "apiPort", "local_api_port", "api_port", "port" }) if (TryFindExactPort(document.RootElement, name, out var port)) return port;
                throw new InvalidOperationException("В gui_settings.json не найден порт ESM API.");
            }
            catch (Exception ex) when ((ex is IOException or JsonException) && attempt < 4) { await Task.Delay(500, token); }
        }
        throw new InvalidOperationException("Не удалось прочитать gui_settings.json ESM.");
    }
    private static bool TryFindExactPort(JsonElement element, string propertyName, out int port)
    {
        port = 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out port) && port is > 0 and < 65536) return true;
                if (TryFindExactPort(property.Value, propertyName, out port)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) if (TryFindExactPort(child, propertyName, out port)) return true;
        return false;
    }
    private static async Task<Program.GismtStatus> ParseStatusAsync(HttpResponseMessage response, CancellationToken token)
    {
        var statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode) return new Program.GismtStatus(null, null, null, null, null, null, statusCode, "ESM status API вернул HTTP " + statusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token)); var gismt = GetProperty(document.RootElement, "gismt");
        if (gismt.ValueKind != JsonValueKind.Object) return new Program.GismtStatus(null, null, null, null, null, null, statusCode, "В ESM status отсутствует объект gismt");
        return new Program.GismtStatus(SafeValue(GetProperty(gismt, "code")), SafeValue(GetProperty(gismt, "error")), SafeValue(GetProperty(gismt, "lastConnection")), SafeValue(GetProperty(gismt, "name")), SafeValue(GetProperty(gismt, "version")), SafeValue(GetProperty(gismt, "id")), statusCode, null);
    }
    private static JsonElement GetProperty(JsonElement element, string name) { if (element.ValueKind == JsonValueKind.Object) foreach (var p in element.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value; return default; }
    private static object? SafeValue(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.TryGetInt64(out var n) ? n : value.GetDouble(), JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    private static string SafeError(Exception ex) => ex is HttpRequestException ? "локальный ESM API недоступен" : ex is TaskCanceledException ? "превышено время ожидания локального ESM API" : ex.GetType().Name;
}

internal sealed class LogTailer
{
    private static readonly Regex MainName = new(@"^esm-cm_\d+\.log$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string _directory;
    public LogTailer(string directory) => _directory = directory;
    internal static bool IsAcceptedLogName(string name) => MainName.IsMatch(name);
    public async IAsyncEnumerable<LogLine> ReadLinesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var files = new Dictionary<string, TailedFile>(StringComparer.OrdinalIgnoreCase); var started = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var path in FindLogs()) if (!files.ContainsKey(path))
                {
                    try { files[path] = new TailedFile(path, started); Console.WriteLine($"\nWatching:\n{path}"); }
                    catch (IOException) { }
                }
                started = true;
                foreach (var entry in files.ToList())
                {
                    var file = entry.Value;
                    if (!File.Exists(file.Path)) { file.Dispose(); files.Remove(entry.Key); continue; }
                    List<string>? available = null;
                    try
                    {
                        available = new List<string>();
                        while (true) { var text = await file.ReadLineAsync(); if (text == null) break; available.Add(text); }
                    }
                    catch (IOException) { file.Dispose(); files.Remove(entry.Key); }
                    if (available != null) foreach (var text in available) yield return new LogLine(file.Path, text);
                }
                await Task.Delay(50, token);
            }
        }
        finally { foreach (var file in files.Values) file.Dispose(); }
    }
    private IEnumerable<string> FindLogs()
    {
        try { return Directory.EnumerateFiles(_directory).Where(path => IsAcceptedLogName(Path.GetFileName(path))).ToList(); }
        catch (IOException) { return Array.Empty<string>(); } catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
    private sealed class TailedFile : IDisposable
    {
        private FileStream? _stream; private StreamReader? _reader; private DateTime _creationTimeUtc; public string Path { get; }
        public TailedFile(string path, bool readFromStart) { Path = path; Open(readFromStart); }
        public async Task<string?> ReadLineAsync()
        {
            if (_stream == null || _reader == null) Open(readFromStart: true);
            var currentCreationTime = File.GetCreationTimeUtc(Path);
            if (_stream!.Length < _stream.Position || currentCreationTime != _creationTimeUtc) { Dispose(); Open(readFromStart: true); }
            return await _reader!.ReadLineAsync();
        }
        private void Open(bool readFromStart) { _creationTimeUtc = File.GetCreationTimeUtc(Path); _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); if (!readFromStart) _stream.Seek(0, SeekOrigin.End); _reader = new StreamReader(_stream); }
        public void Dispose() { _reader?.Dispose(); _stream?.Dispose(); _reader = null; _stream = null; }
    }
}

internal static class ProbeSelfTests
{
    public static Task<int> RunAsync()
    {
        try
        {
            Assert(LogTailer.IsAcceptedLogName("esm-cm_00106704625822.log"), "main log name");
            Assert(!LogTailer.IsAcceptedLogName("esm-cm-events_00106704625822.log"), "events log rejected");
            Assert(!LogTailer.IsAcceptedLogName("esm-cm_00106704625822-2026-08-15.log"), "archive rejected");
            Assert(!LogTailer.IsAcceptedLogName("esm-orchestrator.log"), "other log rejected");

            var parser = new EsmHttpBlockParser();
            var source = "esm-cm_00106704625822.log";
            var lines = new[]
            {
                "2026.07.17 11:24:01.872 DBG", "========== HTTP Request ==========", "Method: POST", "URL: https://cdn03.crpt.ru:19101/api/v4/cdn/codes/check", "Headers:", "Body:", "==================================",
                "========== Время ответа: 61.6819ms ==========" , "========== HTTP Response ==========", "Status: 200 OK (200)", "Headers:", "Body:", "{", "  \"code\": 0,", "  \"description\": \"ok\",", "  \"nested\": { \"codes\": [\"REDACTED\"] }", "}", "=================================="
            };
            var results = lines.SelectMany(x => parser.Process(new LogLine(source, x))).ToList();
            Assert(results.Count == 1, "one multiline transaction");
            var tx = results[0]; Assert(tx.Method == "POST" && tx.Endpoint == "/api/v4/cdn/codes/check", "request metadata"); Assert(tx.HttpStatus == 200 && tx.ProtocolCode == 0 && tx.Description == "ok", "response metadata"); Assert(Math.Abs((tx.EstimatedRequestTime!.Value - tx.BlockTime).TotalMilliseconds + 61.6819) < 0.01, "reconstructed request time");
            foreach (var test in new[] { ("POST", "/api/v4/auth", "status"), ("GET", "/api/v4/cdn/health/check", "code"), ("GET", "/api/v4/cdn/getconf", "code") })
            {
                var parsed = ParseCompleteBlock(test.Item1, test.Item2, test.Item3);
                Assert(parsed.Method == test.Item1 && parsed.Endpoint == test.Item2 && parsed.ProtocolCode == 0, "endpoint block " + test.Item2);
            }

            var buffer = new GismtSampleBuffer(TimeSpan.FromSeconds(10)); var baseTime = tx.BlockTime;
            buffer.Add(GismtSample.Empty(baseTime.AddMilliseconds(-70), "a")); buffer.Add(GismtSample.Empty(baseTime.AddMilliseconds(-60), "b")); buffer.Add(GismtSample.Empty(baseTime, "c")); buffer.Add(GismtSample.Empty(baseTime.AddMilliseconds(500), "d"));
            Assert(buffer.Nearest(tx.EstimatedRequestTime.Value)!.Status.RequestError == "b", "request sample selection"); Assert(buffer.Nearest(baseTime)!.Status.RequestError == "c", "response sample selection"); Assert(buffer.Nearest(baseTime.AddMilliseconds(500))!.Status.RequestError == "d", "after sample selection");

            var failure = parser.Process(new LogLine(source, "2026.07.17 11:25:00.000 ERR Ошибка выполнения Post запроса на новый API Post \"https://cdn06.crpt.ru:19101/api/v4/auth\": context deadline exceeded")).Single();
            Assert(failure.IsFailure && failure.Method == "POST" && failure.TransportError == "timeout" && failure.HttpStatus == null, "transport timeout");
            var output = JsonSerializer.Serialize(new Snapshot(tx, "RESPONSE", 51077, "instance", GismtSample.Empty(baseTime, "safe")));
            Assert(!output.Contains("REDACTED", StringComparison.Ordinal) && !output.Contains("nested", StringComparison.OrdinalIgnoreCase), "output excludes response body");
            Console.WriteLine("GISMTCorrelationProbe self-tests: PASS"); return Task.FromResult(0);
        }
        catch (Exception ex) { Console.Error.WriteLine("GISMTCorrelationProbe self-tests: FAIL: " + ex.Message); return Task.FromResult(1); }
    }
    private static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static CdnTransaction ParseCompleteBlock(string method, string endpoint, string protocolField)
    {
        var parser = new EsmHttpBlockParser();
        var lines = new[] { "2026.07.17 11:24:02.000 DBG", "========== HTTP Request ==========", "Method: " + method, "URL: https://cdn05.crpt.ru:19101" + endpoint, "==================================", "========== Время ответа: 28.2134ms ==========", "========== HTTP Response ==========", "Status: 200 OK", "Body:", "{", "\"" + protocolField + "\": 0,", "\"description\": \"ok\"", "}", "==================================" };
        return lines.SelectMany(line => parser.Process(new LogLine("fixture", line))).Single();
    }
}
