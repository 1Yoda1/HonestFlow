using System.Globalization;
using System.Net;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GISMTCorrelationProbe;

internal static class Program
{
    private const string LogDirectory = @"C:\ProgramData\ESP\ESM\um\log";

    private static readonly Regex RequestRegex = new(
        "(?<method>POST|GET)\\s+(?<url>https://(?<host>cdn\\d+\\.crpt\\.ru):19101(?<endpoint>/api/v4/(?:cdn/codes/check|auth|cdn/getconf|cdn/health/check))(?:[^\\s\\\"']*)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ResponseRegex = new(@"\b(?:HTTP(?:/\d(?:\.\d)?)?\s*|status\s*[:=]\s*)(?<status>[1-5]\d\d)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimeRegex = new(@"(?<time>\d+(?:[.,]\d+)?)\s*ms\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProtocolCodeRegex = new("(?:\\\"code\\\"|\\bcode)\\s*[:=]\\s*(?<code>-?\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DescriptionRegex = new("(?:\\\"description\\\"|\\bdescription)\\s*[:=]\\s*\\\"(?<value>[^\\\"\\r\\n]{0,500})\\\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<int> Main()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _stop.Cancel(); };
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintHeader();

        var outputPath = CreateOutputPath();
        await using var writer = new NdjsonWriter(outputPath);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var esm = new EsmApi(client);

        try
        {
            await esm.InitializeAsync(_stop.Token);
            Console.WriteLine($"\nESM API:\n127.0.0.1:{esm.Port}\n\nInstance:\n{esm.InstanceId}\n\nOutput:\n{outputPath}");
            await WriteSnapshotAsync(writer, esm, new CdnEvent("baseline", DateTimeOffset.UtcNow, null, null, null), "BASELINE", _stop.Token);
            Console.WriteLine("\nWaiting for CDN activity...");
            var tailer = new LogTailer(LogDirectory);
            await foreach (var line in tailer.ReadNewLinesAsync(_stop.Token))
                await ProcessLineAsync(line, esm, writer, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nОшибка запуска: {SafeError(ex)}");
            Console.Error.WriteLine("Нажмите любую клавишу для завершения.");
            Console.ReadKey(intercept: true);
            return 1;
        }
        finally
        {
            await writer.FlushAsync();
            Console.WriteLine($"\nNDJSON сохранён:\n{outputPath}");
        }
        return 0;
    }

    private static readonly CancellationTokenSource _stop = new();
    private static readonly List<CdnEvent> Pending = new();

    private static async Task ProcessLineAsync(string line, EsmApi esm, NdjsonWriter writer, CancellationToken token)
    {
        var request = RequestRegex.Match(line);
        if (request.Success)
        {
            var requestEvent = new CdnEvent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                request.Groups["method"].Value.ToUpperInvariant(), request.Groups["host"].Value.ToLowerInvariant(), request.Groups["endpoint"].Value);
            lock (Pending) Pending.Add(requestEvent);
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss.fff}] CDN REQUEST\n{requestEvent.Method} {requestEvent.Host} {requestEvent.Endpoint}");
            await WriteSnapshotAsync(writer, esm, requestEvent, "REQUEST", token);
            return;
        }

        var response = ResponseRegex.Match(line);
        if (!response.Success) return;
        CdnEvent? evt;
        lock (Pending)
        {
            evt = Pending.FirstOrDefault(x => !x.ResponseSeen);
            if (evt != null) evt.ResponseSeen = true;
            Pending.RemoveAll(x => x.ResponseSeen && DateTimeOffset.UtcNow - x.RequestTime > TimeSpan.FromMinutes(2));
        }
        if (evt == null) return;

        evt.HttpStatus = int.Parse(response.Groups["status"].Value, CultureInfo.InvariantCulture);
        var time = TimeRegex.Match(line);
        if (time.Success && double.TryParse(time.Groups["time"].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var milliseconds)) evt.ResponseTimeMs = milliseconds;
        var code = ProtocolCodeRegex.Match(line);
        if (code.Success && int.TryParse(code.Groups["code"].Value, out var protocolCode)) evt.ProtocolCode = protocolCode;
        var description = DescriptionRegex.Match(line);
        if (description.Success) evt.Description = SafeProtocolDescription(description.Groups["value"].Value);

        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss.fff}] CDN RESPONSE\nHTTP={evt.HttpStatus?.ToString() ?? "-"}\ncode={evt.ProtocolCode?.ToString() ?? "-"}\ntime={evt.ResponseTimeMs?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms");
        await WriteSnapshotAsync(writer, esm, evt, "RESPONSE", token);
        _ = WriteAfterAsync(writer, esm, evt, _stop.Token);
    }

    private static async Task WriteAfterAsync(NdjsonWriter writer, EsmApi esm, CdnEvent evt, CancellationToken token)
    {
        try { await Task.Delay(500, token); await WriteSnapshotAsync(writer, esm, evt, "AFTER_500MS", token); }
        catch (OperationCanceledException) { }
    }

    private static async Task WriteSnapshotAsync(NdjsonWriter writer, EsmApi esm, CdnEvent evt, string phase, CancellationToken token)
    {
        var status = await esm.GetGismtStatusAsync(token);
        var record = new Snapshot(evt, phase, esm.Port, esm.InstanceId, status);
        await writer.WriteAsync(record, token);
        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss.fff}] {phase.Replace("_", " ")}\ngismt.code={Display(status.Code)}\nerror={Display(status.Error)}\nlastConnection={Display(status.LastConnection)}");
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
        var directory = AppContext.BaseDirectory;
        var stem = "gis-correlation_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        for (var i = 0; ; i++)
        {
            var suffix = i == 0 ? "" : $"_{i:D2}";
            var path = Path.Combine(directory, stem + suffix + ".ndjson");
            if (!File.Exists(path)) return path;
        }
    }
    private static string SafeError(Exception ex) => ex is HttpRequestException ? "локальный ESM API недоступен" : ex is TaskCanceledException ? "превышено время ожидания локального ESM API" : ex.GetType().Name;
    private static string? SafeProtocolDescription(string value)
    {
        // A free-form value from a CDN response is deliberately not diagnostic output by default.
        // Preserve only ordinary human-readable phrases; reject opaque identifiers and known secret/header names.
        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > 160 || Regex.IsMatch(trimmed, @"(?i)(token|certificate|x-fn-sid|x-crc32|x-reqid|x-trnid|\bfn\b)")) return null;
        return trimmed.Contains(' ') && Regex.IsMatch(trimmed, @"^[\p{L}\p{N}\s.,:;!?()\-]+$") ? trimmed : null;
    }

    private sealed class CdnEvent
    {
        public CdnEvent(string id, DateTimeOffset time, string? method, string? host, string? endpoint) { Id = id; RequestTime = time; Method = method; Host = host; Endpoint = endpoint; }
        public string Id { get; } public DateTimeOffset RequestTime { get; } public string? Method { get; } public string? Host { get; } public string? Endpoint { get; }
        public bool ResponseSeen { get; set; } public int? HttpStatus { get; set; } public int? ProtocolCode { get; set; } public string? Description { get; set; } public double? ResponseTimeMs { get; set; }
    }

    internal sealed record GismtStatus(object? Code, object? Error, object? LastConnection, object? Name, object? Version, object? Id, int? HttpStatus, string? RequestError);
    private sealed record Snapshot
    {
        public Snapshot(CdnEvent evt, string phase, int port, string? instanceId, GismtStatus status)
        {
            eventId = evt.Id; cdnRequestTime = evt.Method is null ? null : evt.RequestTime; snapshotTime = DateTimeOffset.UtcNow; this.phase = phase;
            cdnHost = evt.Host; method = evt.Method; endpoint = evt.Endpoint; cdnHttpStatus = evt.HttpStatus; cdnProtocolCode = evt.ProtocolCode; cdnDescription = evt.Description; cdnResponseTimeMs = evt.ResponseTimeMs;
            esmPort = port; this.instanceId = instanceId; gismtCode = status.Code; gismtError = status.Error; gismtLastConnection = status.LastConnection; gismtName = status.Name; gismtVersion = status.Version; gismtId = status.Id; esmStatusHttpStatus = status.HttpStatus; esmStatusError = status.RequestError;
        }
        public string eventId { get; } public DateTimeOffset? cdnRequestTime { get; } public DateTimeOffset snapshotTime { get; } public string phase { get; }
        public string? cdnHost { get; } public string? method { get; } public string? endpoint { get; } public int? cdnHttpStatus { get; } public int? cdnProtocolCode { get; } public string? cdnDescription { get; } public double? cdnResponseTimeMs { get; }
        public int esmPort { get; } public string? instanceId { get; } public object? gismtCode { get; } public object? gismtError { get; } public object? gismtLastConnection { get; } public object? gismtName { get; } public object? gismtVersion { get; } public object? gismtId { get; } public int? esmStatusHttpStatus { get; } public string? esmStatusError { get; }
    }
}

internal sealed class NdjsonWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public NdjsonWriter(string path) => _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
    public async Task WriteAsync<T>(T record, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        await _gate.WaitAsync(token);
        try { await _writer.WriteLineAsync(json); await _writer.FlushAsync(); }
        finally { _gate.Release(); }
    }
    public Task FlushAsync() => _writer.FlushAsync();
    public async ValueTask DisposeAsync() { await _writer.DisposeAsync(); _gate.Dispose(); }
}

internal sealed class EsmApi
{
    private readonly HttpClient _client;
    public EsmApi(HttpClient client) => _client = client;
    public int Port { get; private set; }
    public string? InstanceId { get; private set; }

    public async Task InitializeAsync(CancellationToken token)
    {
        Port = await ReadPortAsync(token);
        await RefreshInstanceAsync(token);
    }

    public async Task<Program.GismtStatus> GetGismtStatusAsync(CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(InstanceId)) await RefreshInstanceAsync(token);
            using var response = await _client.GetAsync($"http://127.0.0.1:{Port}/api/v1/status/{Uri.EscapeDataString(InstanceId!)}", token);
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
            {
                await RefreshInstanceAsync(token);
                using var retry = await _client.GetAsync($"http://127.0.0.1:{Port}/api/v1/status/{Uri.EscapeDataString(InstanceId!)}", token);
                return await ParseStatusAsync(retry, token);
            }
            return await ParseStatusAsync(response, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Program.GismtStatus(null, null, null, null, null, null, null, SafeError(ex));
        }
    }

    private async Task RefreshInstanceAsync(CancellationToken token)
    {
        using var response = await _client.GetAsync($"http://127.0.0.1:{Port}/api/v1/instances/info", token);
        response.EnsureSuccessStatusCode();
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
        // gui_settings is owned by ESM; retry makes startup tolerant of a transient rewrite.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(@"C:\ProgramData\ESP\ESM\esm-gui\gui_settings.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (TryFindPort(document.RootElement, out var port)) return port;
                throw new InvalidOperationException("В gui_settings.json не найден порт ESM API.");
            }
            catch (Exception ex) when ((ex is IOException or JsonException) && attempt < 4)
            {
                await Task.Delay(500, token);
            }
        }
        throw new InvalidOperationException("Не удалось прочитать gui_settings.json ESM.");
    }

    private static bool TryFindPort(JsonElement element, out int port)
    {
        port = 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Contains("port", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out port) && port is > 0 and < 65536) return true;
                if (TryFindPort(property.Value, out port)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) if (TryFindPort(child, out port)) return true;
        return false;
    }

    private static async Task<Program.GismtStatus> ParseStatusAsync(HttpResponseMessage response, CancellationToken token)
    {
        var statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode) return new Program.GismtStatus(null, null, null, null, null, null, statusCode, "ESM status API вернул HTTP " + statusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
        var gismt = GetProperty(document.RootElement, "gismt");
        if (gismt.ValueKind != JsonValueKind.Object) return new Program.GismtStatus(null, null, null, null, null, null, statusCode, "В ESM status отсутствует объект gismt");
        return new Program.GismtStatus(SafeValue(GetProperty(gismt, "code")), SafeValue(GetProperty(gismt, "error")), SafeValue(GetProperty(gismt, "lastConnection")), SafeValue(GetProperty(gismt, "name")), SafeValue(GetProperty(gismt, "version")), SafeValue(GetProperty(gismt, "id")), statusCode, null);
    }
    private static JsonElement GetProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }
    private static object? SafeValue(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.TryGetInt64(out var n) ? n : value.GetDouble(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null or JsonValueKind.Undefined => null, _ => null };
    private static string SafeError(Exception ex) => ex is HttpRequestException ? "локальный ESM API недоступен" : ex is TaskCanceledException ? "превышено время ожидания локального ESM API" : ex.GetType().Name;
}

internal sealed class LogTailer
{
    private static readonly Regex MainEsmLogNameRegex = new(@"^esm-cm_\d+\.log$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly string _directory;
    public LogTailer(string directory) => _directory = directory;
    public async IAsyncEnumerable<string> ReadNewLinesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        string? currentPath = null;
        FileStream? stream = null;
        StreamReader? reader = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var selected = FindCurrentLog();
                if (selected == null) { await Task.Delay(500, token); continue; }
                if (!string.Equals(selected, currentPath, StringComparison.OrdinalIgnoreCase) || stream == null)
                {
                    reader?.Dispose(); stream?.Dispose();
                    try
                    {
                        stream = new FileStream(selected, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        stream.Seek(0, SeekOrigin.End); reader = new StreamReader(stream); currentPath = selected;
                        Console.WriteLine($"\nESM log:\n{selected}");
                    }
                    catch (IOException) { stream = null; reader = null; await Task.Delay(500, token); continue; }
                }
                var line = await reader!.ReadLineAsync();
                if (line != null) { yield return line; continue; }
                if (!File.Exists(currentPath) || stream.Length < stream.Position) { reader.Dispose(); stream.Dispose(); reader = null; stream = null; currentPath = null; }
                await Task.Delay(100, token);
            }
        }
        finally { reader?.Dispose(); stream?.Dispose(); }
    }
    private string? FindCurrentLog()
    {
        try
        {
            return Directory.EnumerateFiles(_directory)
                .Where(path => MainEsmLogNameRegex.IsMatch(Path.GetFileName(path)))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
