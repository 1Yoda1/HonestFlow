using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure
{
    public sealed class GisMtDiagnosticProbe : IGisMtDiagnosticProbe
    {
        public const int DefaultLogTailBytes = 512 * 1024;
        public static readonly TimeSpan LogEvidenceFreshness = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan CacheFreshness = TimeSpan.FromHours(1);
        public const string DefaultUmDirectory = @"C:\ProgramData\ESP\ESM\um";

        private readonly string _umDirectory;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly int _logTailBytes;

        public GisMtDiagnosticProbe(
            string umDirectory = DefaultUmDirectory,
            Func<DateTimeOffset> utcNow = null,
            int logTailBytes = DefaultLogTailBytes)
        {
            _umDirectory = umDirectory ?? throw new ArgumentNullException(nameof(umDirectory));
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _logTailBytes = logTailBytes > 0 ? logTailBytes : throw new ArgumentOutOfRangeException(nameof(logTailBytes));
        }

        public Task<GisMtDiagnosticResult> CheckAsync(
            EsmStatusResult localRestStatus,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(() => Check(localRestStatus, cancellationToken), cancellationToken);
        }

        private GisMtDiagnosticResult Check(EsmStatusResult localRestStatus, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _utcNow();
            string instanceId = SafeInstanceId(localRestStatus?.InstanceId);
            ConfigEvidence config = ReadConfig(instanceId);
            cancellationToken.ThrowIfCancellationRequested();
            CacheEvidence cache = ReadCache(instanceId, now);
            cancellationToken.ThrowIfCancellationRequested();
            LogEvidence log = ReadLog(instanceId, now);
            cancellationToken.ThrowIfCancellationRequested();

            EsmComponentStatus rest = EffectiveStatus(localRestStatus?.Status)?.Gismt;
            return Evaluate(rest, config, cache, log, now);
        }

        private static EsmStatusDto EffectiveStatus(EsmStatusDto status) =>
            status?.Software?.Data ?? status?.Data?.Software?.Data ?? status?.Data ?? status;

        private GisMtDiagnosticResult Evaluate(
            EsmComponentStatus rest,
            ConfigEvidence config,
            CacheEvidence cache,
            LogEvidence log,
            DateTimeOffset now)
        {
            DateTimeOffset? restTimestamp = ParseTimestamp(rest?.LastConnection);
            bool freshTransportEvidence = IsFresh(log.CdnTransportEvidenceUtc, now);
            bool freshApplicationEvidence = IsFresh(log.ApplicationEvidenceUtc, now);
            bool freshControlledChannelEvidence = IsFresh(log.ControlledChannelEvidenceUtc, now);
            bool controlledChannelReady = freshControlledChannelEvidence &&
                                          log.ControlledChannelState == GisMtEvidenceState.Healthy;
            bool transportErrorIsCurrent = freshTransportEvidence &&
                log.CdnTransportState == GisMtEvidenceState.Error &&
                (!restTimestamp.HasValue || log.CdnTransportEvidenceUtc >= restTimestamp) &&
                (!controlledChannelReady || log.CdnTransportEvidenceUtc > log.ControlledChannelEvidenceUtc);
            bool applicationErrorIsCurrent = freshApplicationEvidence &&
                log.ApplicationExchangeState == GisMtEvidenceState.Error &&
                (!restTimestamp.HasValue || log.ApplicationEvidenceUtc >= restTimestamp) &&
                (!controlledChannelReady || log.ApplicationEvidenceUtc > log.ControlledChannelEvidenceUtc);
            bool restErrorIsCurrent = rest?.Code.HasValue == true && rest.Code.Value != 0 &&
                                      (!controlledChannelReady ||
                                       (restTimestamp.HasValue && restTimestamp > log.ControlledChannelEvidenceUtc));
            bool allCachedBlocked = cache.Count > 0 && cache.AvailableCount == 0;
            bool mismatch = !string.IsNullOrWhiteSpace(log.LogicalCdn) &&
                            !string.IsNullOrWhiteSpace(log.ActualCdn) &&
                            !string.Equals(log.LogicalCdn, log.ActualCdn, StringComparison.OrdinalIgnoreCase);

            GisMtDiagnosticState state;
            GisMtErrorKind errorKind = GisMtErrorKind.None;
            string summary;

            if (transportErrorIsCurrent)
            {
                state = GisMtDiagnosticState.Error;
                errorKind = GisMtErrorKind.Connectivity;
                summary = SummaryFor(errorKind);
            }
            else if (applicationErrorIsCurrent)
            {
                state = GisMtDiagnosticState.Error;
                errorKind = log.ApplicationErrorKind == GisMtErrorKind.None
                    ? GisMtErrorKind.Application
                    : log.ApplicationErrorKind;
                summary = SummaryFor(errorKind);
            }
            else if (restErrorIsCurrent)
            {
                errorKind = ClassifyError(rest.Error);
                state = errorKind == GisMtErrorKind.ControlledChannel
                    ? GisMtDiagnosticState.Warning
                    : GisMtDiagnosticState.Error;
                summary = SummaryFor(errorKind);
            }
            else if (controlledChannelReady)
            {
                state = GisMtDiagnosticState.Healthy;
                summary = "Работает";
            }
            else if (!config.FileFound || !config.HasGisMtSection || config.CdnHosts.Count == 0)
            {
                state = GisMtDiagnosticState.Error;
                errorKind = GisMtErrorKind.Configuration;
                summary = "Не получена конфигурация CDN";
            }
            else if (allCachedBlocked)
            {
                state = rest?.Code == 0 ? GisMtDiagnosticState.Warning : GisMtDiagnosticState.Error;
                errorKind = GisMtErrorKind.NoAvailableCdn;
                summary = "Нет доступных CDN-площадок";
            }
            else if (rest?.Code == 0)
            {
                state = GisMtDiagnosticState.Healthy;
                summary = "Работает";
            }
            else if (freshApplicationEvidence && log.ApplicationExchangeState == GisMtEvidenceState.Healthy)
            {
                state = GisMtDiagnosticState.Healthy;
                summary = "Работает";
            }
            else if (freshTransportEvidence && log.CdnTransportState == GisMtEvidenceState.Healthy &&
                     freshControlledChannelEvidence && log.ControlledChannelState == GisMtEvidenceState.Error)
            {
                state = GisMtDiagnosticState.Warning;
                errorKind = GisMtErrorKind.ControlledChannel;
                summary = SummaryFor(errorKind);
            }
            else
            {
                state = GisMtDiagnosticState.Unknown;
                summary = "Не удалось проверить";
            }

            var evidence = new List<string>
            {
                $"Local REST gismt.code: {rest?.Code?.ToString(CultureInfo.InvariantCulture) ?? "не получен"}",
                $"CDN в конфигурации: {config.CdnHosts.Count}",
                $"CDN в cache: {cache.Count}",
                $"Не заблокировано по CDN cache: {cache.AvailableCount}",
                $"Заблокировано: {cache.BlockedCount}"
            };
            if (cache.LastCheckedUtc.HasValue)
                evidence.Add($"Последняя проверка CDN cache: {cache.LastCheckedUtc.Value:O}");
            else
                evidence.Add("Последняя проверка CDN cache: нет данных");
            evidence.Add("Актуальность CDN cache: " +
                (cache.LastCheckedUtc.HasValue
                    ? now - cache.LastCheckedUtc.Value <= CacheFreshness ? "актуален" : "устарел"
                    : "неизвестна"));
            if (cache.LastLatencyMs.HasValue)
                evidence.Add($"Последняя latency CDN: {cache.LastLatencyMs.Value.ToString("0.###", CultureInfo.InvariantCulture)} ms");
            if (!string.IsNullOrWhiteSpace(log.ActualCdn))
                evidence.Add("Последний фактический CDN: " + log.ActualCdn);
            if (log.LastCdnTransportSuccessUtc.HasValue)
                evidence.Add($"Последняя связь с CDN: {log.LastCdnTransportSuccessUtc.Value:O}");
            if (log.LastSuccessfulGisExchangeUtc.HasValue)
                evidence.Add($"Последний успешный обмен с ГИС МТ: {log.LastSuccessfulGisExchangeUtc.Value:O}");
            if (log.CdnTransportState == GisMtEvidenceState.Error && !string.IsNullOrWhiteSpace(log.LastCdnTransportError))
                evidence.Add(WithTimestamp("Последняя ошибка транспорта CDN", log.LastCdnTransportError, log.LastCdnTransportErrorUtc));
            if (log.ApplicationExchangeState == GisMtEvidenceState.Error && !string.IsNullOrWhiteSpace(log.LastApplicationError))
                evidence.Add(WithTimestamp("Последняя ошибка обмена с ГИС МТ", log.LastApplicationError, log.LastApplicationErrorUtc));
            evidence.Add("Controlled channel: " + EvidenceStateText(log.ControlledChannelState));
            if (log.ControlledChannelState == GisMtEvidenceState.Error && !string.IsNullOrWhiteSpace(log.LastControlledChannelError))
                evidence.Add(WithTimestamp("Последняя ошибка protected channel", log.LastControlledChannelError, log.LastControlledChannelErrorUtc));
            if (mismatch)
                evidence.Add("Возможна ошибка переключения CDN");
            if (rest?.Code != 0 && cache.AvailableCount > 0)
                evidence.Add("В CDN cache имеются незаблокированные площадки");

            string[] safeEvidence = evidence
                .Select(GisMtSecretRedactor.Redact)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            return new GisMtDiagnosticResult
            {
                State = state,
                Summary = summary,
                Details = string.Join(Environment.NewLine, safeEvidence),
                ConfiguredCdnCount = config.CdnHosts.Count,
                ConfiguredCdns = config.CdnHosts.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                CachedCdnCount = cache.Count,
                AvailableCdnCount = cache.AvailableCount,
                BlockedCdnCount = cache.BlockedCount,
                CacheIsFresh = cache.LastCheckedUtc.HasValue ? now - cache.LastCheckedUtc.Value <= CacheFreshness : null,
                CacheLastCheckedUtc = cache.LastCheckedUtc,
                LastLatencyMs = cache.LastLatencyMs,
                LastActualCdn = log.ActualCdn,
                LastLogicalCdn = log.LogicalCdn,
                CdnTransportState = freshTransportEvidence ? log.CdnTransportState : GisMtEvidenceState.Unknown,
                LastCdnTransportSuccessUtc = log.LastCdnTransportSuccessUtc,
                LastCdnTransportErrorUtc = log.LastCdnTransportErrorUtc,
                LastCdnTransportError = GisMtSecretRedactor.Redact(log.LastCdnTransportError),
                ApplicationExchangeState = freshApplicationEvidence ? log.ApplicationExchangeState : GisMtEvidenceState.Unknown,
                LastSuccessfulGisExchangeUtc = log.LastSuccessfulGisExchangeUtc,
                LastApplicationErrorUtc = log.LastApplicationErrorUtc,
                LastApplicationError = GisMtSecretRedactor.Redact(log.LastApplicationError),
                LastApplicationErrorKind = log.ApplicationErrorKind,
                ControlledChannelState = freshControlledChannelEvidence ? log.ControlledChannelState : GisMtEvidenceState.Unknown,
                LastControlledChannelSuccessUtc = log.LastControlledChannelSuccessUtc,
                LastControlledChannelErrorUtc = log.LastControlledChannelErrorUtc,
                LastControlledChannelError = GisMtSecretRedactor.Redact(log.LastControlledChannelError),
                EvidenceTimestampUtc = Max(restTimestamp, log.LatestTimestampUtc, cache.LastCheckedUtc),
                PossibleCdnStateMismatch = mismatch,
                ControlledChannelDetails = GisMtSecretRedactor.Redact(log.LastControlledChannelError),
                LogBytesRead = log.BytesRead,
                Evidence = safeEvidence
            };
        }

        private ConfigEvidence ReadConfig(string instanceId)
        {
            string path = ResolveFile($"config_{instanceId}.yml", "config_*.yml", _umDirectory);
            if (path == null) return ConfigEvidence.Missing();

            try
            {
                string[] lines = ReadSharedLines(path);
                int sectionIndent = -1;
                bool inSection = false;
                string pendingHost = null;
                var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in lines)
                {
                    string line = StripComment(raw);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int indent = LeadingWhitespace(line);
                    if (!inSection)
                    {
                        if (!Regex.IsMatch(line, @"^\s*gisMT\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
                        inSection = true;
                        sectionIndent = indent;
                        continue;
                    }

                    if (indent <= sectionIndent) break;
                    string trimmed = line.Trim();
                    bool directRegistrationUrl = indent <= sectionIndent + 2 &&
                        Regex.IsMatch(trimmed, @"^url\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (directRegistrationUrl) continue;
                    foreach (Match match in HostPortRegex.Matches(trimmed))
                    {
                        string host = NormalizeHostPort(match.Groups["host"].Value, match.Groups["port"].Value);
                        if (!string.Equals(host, "ts-reg.crpt.ru:19100", StringComparison.OrdinalIgnoreCase))
                            hosts.Add(host);
                    }
                    Match hostMatch = Regex.Match(trimmed, @"^(?:-\s*)?host\s*:\s*[""']?(?<host>[a-z0-9.-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (hostMatch.Success)
                        pendingHost = hostMatch.Groups["host"].Value;
                    Match portMatch = Regex.Match(trimmed, @"^port\s*:\s*(?<port>\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (portMatch.Success && !string.IsNullOrWhiteSpace(pendingHost))
                    {
                        hosts.Add(NormalizeHostPort(pendingHost, portMatch.Groups["port"].Value));
                        pendingHost = null;
                    }
                }
                return new ConfigEvidence(true, inSection, hosts.ToArray());
            }
            catch (IOException) { return new ConfigEvidence(true, false, Array.Empty<string>()); }
            catch (UnauthorizedAccessException) { return new ConfigEvidence(true, false, Array.Empty<string>()); }
        }

        private CacheEvidence ReadCache(string instanceId, DateTimeOffset now)
        {
            string cacheDirectory = Path.Combine(_umDirectory, "cache");
            string path = ResolveFile($"{instanceId}_cdn_cache.json", "*_cdn_cache.json", cacheDirectory);
            if (path == null) return CacheEvidence.Empty();
            try
            {
                string json = ReadSharedText(path);
                JToken root = JToken.Parse(json);
                IEnumerable<JToken> tokens = root is JContainer container
                    ? container.Descendants().Prepend(root)
                    : new[] { root };
                JObject[] endpoints = tokens
                    .OfType<JObject>()
                    .Where(item => item.Property("host", StringComparison.OrdinalIgnoreCase) != null)
                    .ToArray();
                int blocked = endpoints.Count(item => ReadBoolean(item, "block") == true || IsBlockedUntil(item, now));
                DateTimeOffset[] checkedAt = endpoints
                    .Select(item => ReadTimestamp(item, "last_checked_utc"))
                    .Where(value => value.HasValue)
                    .Select(value => value.Value)
                    .ToArray();
                DateTimeOffset? latest = checkedAt.Length == 0 ? null : checkedAt.Max();
                double? latency = endpoints
                    .Select(item => ReadDouble(item, "latency_ms"))
                    .FirstOrDefault(value => value.HasValue);
                return new CacheEvidence(endpoints.Length, endpoints.Length - blocked, blocked, latest, latency);
            }
            catch (IOException) { return CacheEvidence.Empty(); }
            catch (UnauthorizedAccessException) { return CacheEvidence.Empty(); }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or FormatException)
            {
                return CacheEvidence.Empty();
            }
        }

        private LogEvidence ReadLog(string instanceId, DateTimeOffset now)
        {
            string logDirectory = Path.Combine(_umDirectory, "log");
            string path = ResolveFile($"esm-cm_{instanceId}.log", "esm-cm_*.log", logDirectory, IsCurrentCmLog);
            if (path == null) return LogEvidence.Empty();
            try
            {
                TailReadResult tail = ReadBoundedTail(path, _logTailBytes);
                return GisMtLogParser.Parse(tail.Text, tail.BytesRead, now);
            }
            catch (IOException) { return LogEvidence.Empty(); }
            catch (UnauthorizedAccessException) { return LogEvidence.Empty(); }
        }

        private static TailReadResult ReadBoundedTail(string path, int maxBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int bytesToRead = (int)Math.Min(stream.Length, maxBytes);
            long start = stream.Length - bytesToRead;
            stream.Seek(start, SeekOrigin.Begin);
            byte[] buffer = new byte[bytesToRead];
            int total = 0;
            while (total < bytesToRead)
            {
                int read = stream.Read(buffer, total, bytesToRead - total);
                if (read == 0) break;
                total += read;
            }
            string text = Encoding.UTF8.GetString(buffer, 0, total);
            if (start > 0)
            {
                int firstLineBreak = text.IndexOf('\n');
                text = firstLineBreak >= 0 ? text.Substring(firstLineBreak + 1) : string.Empty;
            }
            return new TailReadResult(text, total);
        }

        private static string[] ReadSharedLines(string path) =>
            ReadSharedText(path).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        private static string ReadSharedText(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static string ResolveFile(
            string exactName,
            string pattern,
            string directory,
            Func<string, bool> predicate = null)
        {
            try
            {
                if (!Directory.Exists(directory)) return null;
                if (!string.IsNullOrWhiteSpace(exactName) && exactName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                {
                    string exact = Path.Combine(directory, exactName);
                    if (File.Exists(exact)) return exact;
                }
                return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                    .Where(path => predicate == null || predicate(path))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static bool IsCurrentCmLog(string path) =>
            Regex.IsMatch(Path.GetFileName(path), @"^esm-cm_[^-]+\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string SafeInstanceId(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            value.IndexOf(Path.DirectorySeparatorChar) < 0 && value.IndexOf(Path.AltDirectorySeparatorChar) < 0
                ? value.Trim()
                : string.Empty;

        private static string StripComment(string value)
        {
            int comment = value.IndexOf('#');
            return comment >= 0 ? value.Substring(0, comment) : value;
        }

        private static int LeadingWhitespace(string value) => value.TakeWhile(char.IsWhiteSpace).Count();
        private static string NormalizeHostPort(string host, string port) => host.Trim().ToLowerInvariant() + ":" + port;

        private static bool? ReadBoolean(JObject item, string property) =>
            item.GetValue(property, StringComparison.OrdinalIgnoreCase)?.Type == JTokenType.Boolean
                ? item.GetValue(property, StringComparison.OrdinalIgnoreCase)?.Value<bool>()
                : null;

        private static double? ReadDouble(JObject item, string property)
        {
            JToken value = item.GetValue(property, StringComparison.OrdinalIgnoreCase);
            return value != null && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : null;
        }

        private static bool IsBlockedUntil(JObject item, DateTimeOffset now)
        {
            DateTimeOffset? until = ReadTimestamp(item, "block_until_utc");
            return until.HasValue && until.Value > now;
        }

        private static DateTimeOffset? ReadTimestamp(JObject item, string property) =>
            ParseTimestamp(item.GetValue(property, StringComparison.OrdinalIgnoreCase)?.ToString());

        private static DateTimeOffset? ParseTimestamp(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                if (parsed.Year <= DateTimeOffset.MinValue.Year) return null;
                DateTimeOffset utc = parsed.ToUniversalTime();
                return utc.Year <= DateTimeOffset.MinValue.Year ? null : utc;
            }
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unix))
            {
                try { return unix > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix); }
                catch (ArgumentOutOfRangeException) { }
            }
            return null;
        }

        private static DateTimeOffset? Max(params DateTimeOffset?[] values) =>
            values.Where(value => value.HasValue).Select(value => value.Value).DefaultIfEmpty().Max() is var value && value != default ? value : null;

        private static bool IsFresh(DateTimeOffset? timestamp, DateTimeOffset now) =>
            timestamp.HasValue && now - timestamp.Value <= LogEvidenceFreshness;

        private static string EvidenceStateText(GisMtEvidenceState state) => state switch
        {
            GisMtEvidenceState.Healthy => "работает",
            GisMtEvidenceState.Error => "ошибка",
            _ => "нет данных"
        };

        private static string WithTimestamp(string label, string value, DateTimeOffset? timestamp) =>
            timestamp.HasValue ? $"{label}: {value} ({timestamp.Value:O})" : $"{label}: {value}";

        private static GisMtErrorKind ClassifyError(string value)
        {
            string text = value ?? string.Empty;
            if (Contains(text, "регистрац")) return GisMtErrorKind.Registration;
            if (IsTransportFailure(text))
                return GisMtErrorKind.Connectivity;
            if (IsControlledChannelFailure(text)) return GisMtErrorKind.ControlledChannel;
            return string.IsNullOrWhiteSpace(text) ? GisMtErrorKind.Other : GisMtErrorKind.Other;
        }

        private static string SummaryFor(GisMtErrorKind kind) => kind switch
        {
            GisMtErrorKind.Connectivity => "Нет связи с CDN",
            GisMtErrorKind.Application => "Ошибка обмена с ГИС МТ",
            GisMtErrorKind.Registration => "Ошибка регистрации в ГИС МТ",
            GisMtErrorKind.ControlledChannel => "Ошибка защищённого канала",
            GisMtErrorKind.Configuration => "Не получена конфигурация CDN",
            GisMtErrorKind.NoAvailableCdn => "Нет доступных CDN-площадок",
            _ => "Ошибка ГИС МТ"
        };

        private static bool Contains(string value, params string[] parts) =>
            parts.Any(part => value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsTransportFailure(string value) => Contains(value,
            "нет подключения к cdn",
            "connection refused",
            "context deadline exceeded",
            "client.timeout exceeded",
            "no such host",
            "dial tcp",
            "tls handshake",
            "tls/connect",
            "connectex:",
            "error 2016");

        private static bool IsControlledChannelFailure(string value) => Contains(value,
            "controlled channel",
            "контролируем",
            "ошибка установки кк",
            "ошибка шифрования в фн",
            "encryptdata failure",
            "decryptdata failure",
            "ошибка чтения данных из дккт",
            "ошибка из ккт",
            "module:\"ecr\"",
            "module: \"ecr\"",
            "смена закрыта - операция невозможна",
            "error 2021",
            "error 2030");

        private static readonly Regex HostPortRegex = new(
            @"(?:(?:https?://)?(?<host>[a-z0-9.-]+)):(?<port>\d{2,5})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private sealed record ConfigEvidence(bool FileFound, bool HasGisMtSection, IReadOnlyCollection<string> CdnHosts)
        {
            public static ConfigEvidence Missing() => new(false, false, Array.Empty<string>());
        }

        private sealed record CacheEvidence(int Count, int AvailableCount, int BlockedCount, DateTimeOffset? LastCheckedUtc, double? LastLatencyMs)
        {
            public static CacheEvidence Empty() => new(0, 0, 0, null, null);
        }

        private sealed record TailReadResult(string Text, int BytesRead);

        private sealed class LogEvidence
        {
            public string ActualCdn { get; init; }
            public string LogicalCdn { get; init; }
            public GisMtEvidenceState CdnTransportState { get; init; }
            public DateTimeOffset? CdnTransportEvidenceUtc { get; init; }
            public DateTimeOffset? LastCdnTransportSuccessUtc { get; init; }
            public DateTimeOffset? LastCdnTransportErrorUtc { get; init; }
            public string LastCdnTransportError { get; init; }
            public GisMtEvidenceState ApplicationExchangeState { get; init; }
            public DateTimeOffset? ApplicationEvidenceUtc { get; init; }
            public DateTimeOffset? LastSuccessfulGisExchangeUtc { get; init; }
            public DateTimeOffset? LastApplicationErrorUtc { get; init; }
            public string LastApplicationError { get; init; }
            public GisMtErrorKind ApplicationErrorKind { get; init; }
            public GisMtEvidenceState ControlledChannelState { get; init; }
            public DateTimeOffset? ControlledChannelEvidenceUtc { get; init; }
            public DateTimeOffset? LastControlledChannelSuccessUtc { get; init; }
            public DateTimeOffset? LastControlledChannelErrorUtc { get; init; }
            public string LastControlledChannelError { get; init; }
            public DateTimeOffset? LatestTimestampUtc { get; init; }
            public int BytesRead { get; init; }
            public static LogEvidence Empty() => new();
        }

        private static class GisMtLogParser
        {
            private static readonly Regex TimestampRegex = new(
                @"^(?<timestamp>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex HttpUrlRegex = new(
                @"https://(?<host>[a-z0-9.-]+):(?<port>\d{2,5})(?<endpoint>/api/v4/(?:cdn/(?:codes/check|health/check|getconf)|auth))(?=[/?#\s""']|$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            private static readonly Regex LogicalCdnRegex = new(
                @"Устанавливаем[^\r\n]*(?<host>cdn[a-z0-9.-]*\.crpt\.ru)(?::(?<port>\d{2,5}))?[^\r\n]*CDN площадк",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            public static LogEvidence Parse(string text, int bytesRead, DateTimeOffset now)
            {
                string actual = null;
                string logical = null;
                string pendingHost = null;
                string pendingEndpoint = null;
                int? pendingHttpStatus = null;
                DateTimeOffset? currentTimestamp = null;
                DateTimeOffset? pendingTimestamp = null;
                DateTimeOffset? latest = null;
                GisMtEvidenceState transportState = GisMtEvidenceState.Unknown;
                DateTimeOffset? transportEvidenceUtc = null;
                DateTimeOffset? transportSuccessUtc = null;
                DateTimeOffset? transportErrorUtc = null;
                string transportError = null;
                GisMtEvidenceState applicationState = GisMtEvidenceState.Unknown;
                DateTimeOffset? applicationEvidenceUtc = null;
                DateTimeOffset? gisExchangeSuccessUtc = null;
                DateTimeOffset? applicationErrorUtc = null;
                string applicationError = null;
                GisMtErrorKind applicationErrorKind = GisMtErrorKind.None;
                GisMtEvidenceState controlledChannelState = GisMtEvidenceState.Unknown;
                DateTimeOffset? controlledChannelEvidenceUtc = null;
                DateTimeOffset? controlledChannelSuccessUtc = null;
                DateTimeOffset? controlledChannelErrorUtc = null;
                string controlledChannelError = null;

                foreach (string raw in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    string line = GisMtSecretRedactor.Redact(raw);
                    Match timestampMatch = TimestampRegex.Match(line);
                    if (timestampMatch.Success && DateTimeOffset.TryParseExact(
                        timestampMatch.Groups["timestamp"].Value,
                        new[] { "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm:ss.FFFFFFF" },
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal,
                        out DateTimeOffset parsed))
                    {
                        currentTimestamp = parsed.ToUniversalTime();
                        latest = !latest.HasValue || currentTimestamp > latest ? currentTimestamp : latest;
                    }

                    Match url = HttpUrlRegex.Match(line);
                    if (url.Success)
                    {
                        pendingHost = NormalizeHostPort(url.Groups["host"].Value, url.Groups["port"].Value);
                        pendingEndpoint = url.Groups["endpoint"].Value;
                        pendingHttpStatus = null;
                        pendingTimestamp = currentTimestamp;
                    }

                    Match logicalMatch = LogicalCdnRegex.Match(line);
                    if (logicalMatch.Success)
                        logical = NormalizeHostPort(logicalMatch.Groups["host"].Value,
                            logicalMatch.Groups["port"].Success ? logicalMatch.Groups["port"].Value : "19101");

                    bool cdnTransportContext = url.Success ||
                        Regex.IsMatch(line, @"https://cdn[a-z0-9.-]*:\d{2,5}",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || Contains(line,
                        "нет подключения к cdn площадке",
                        "cdn площадк",
                        "getconf",
                        "challenge");
                    bool transportFailure = cdnTransportContext && IsTransportFailure(line);
                    if (currentTimestamp.HasValue && transportFailure && IsNewest(currentTimestamp, transportEvidenceUtc))
                    {
                        transportState = GisMtEvidenceState.Error;
                        transportEvidenceUtc = currentTimestamp;
                        transportErrorUtc = currentTimestamp;
                        transportError = TransportErrorSummary(line);
                    }

                    if (currentTimestamp.HasValue && !transportFailure && IsControlledChannelFailure(line) &&
                        IsNewest(currentTimestamp, controlledChannelEvidenceUtc) &&
                        Contains(line, "ошиб", "failed", "code:73", "code = 73", "error 2021", "error 2030", "смена закрыта"))
                    {
                        controlledChannelState = GisMtEvidenceState.Error;
                        controlledChannelEvidenceUtc = currentTimestamp;
                        controlledChannelErrorUtc = currentTimestamp;
                        controlledChannelError = ControlledChannelErrorSummary(line);
                    }
                    else if (currentTimestamp.HasValue && IsNewest(currentTimestamp, controlledChannelEvidenceUtc) &&
                             (IsControlledChannelReady(line) || Contains(line,
                                 "кк установлен/переустановлен",
                                 "кк установлен в ходе приоритизации")))
                    {
                        controlledChannelState = GisMtEvidenceState.Healthy;
                        controlledChannelEvidenceUtc = currentTimestamp;
                        controlledChannelSuccessUtc = currentTimestamp;
                    }

                    if (currentTimestamp.HasValue && IsNewest(currentTimestamp, applicationEvidenceUtc) &&
                        Contains(line, "Ошибка регистрации в ГИС МТ"))
                    {
                        applicationState = GisMtEvidenceState.Error;
                        applicationEvidenceUtc = currentTimestamp;
                        applicationErrorUtc = currentTimestamp;
                        applicationError = SummaryFor(GisMtErrorKind.Registration);
                        applicationErrorKind = GisMtErrorKind.Registration;
                    }

                    Match httpStatus = Regex.Match(line, @"^\s*Status:\s*(?<status>\d{3})\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (pendingHost != null && httpStatus.Success &&
                        int.TryParse(httpStatus.Groups["status"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int statusCode))
                    {
                        DateTimeOffset? responseTimestamp = currentTimestamp ?? pendingTimestamp;
                        pendingHttpStatus = statusCode;
                        if (IsNewest(responseTimestamp, transportEvidenceUtc))
                        {
                            transportState = GisMtEvidenceState.Healthy;
                            transportEvidenceUtc = responseTimestamp;
                            actual = pendingHost;
                        }
                        if (IsNewest(responseTimestamp, transportSuccessUtc))
                            transportSuccessUtc = responseTimestamp;

                        if (statusCode >= 400 && IsNewest(responseTimestamp, applicationEvidenceUtc))
                        {
                            applicationState = GisMtEvidenceState.Error;
                            applicationEvidenceUtc = responseTimestamp;
                            applicationErrorUtc = responseTimestamp;
                            applicationError = $"HTTP {statusCode.ToString(CultureInfo.InvariantCulture)}";
                            applicationErrorKind = GisMtErrorKind.Application;
                        }
                    }

                    Match responseCode = Regex.Match(line,
                        @"[""']?(?:code|status)[""']?\s*:\s*(?<code>-?\d+)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (pendingHost != null && pendingHttpStatus is >= 200 and < 300 && responseCode.Success &&
                        !string.Equals(pendingEndpoint, "/api/v4/auth", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(responseCode.Groups["code"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) &&
                        IsNewest(currentTimestamp ?? pendingTimestamp, applicationEvidenceUtc))
                    {
                        DateTimeOffset? exchangeTimestamp = currentTimestamp ?? pendingTimestamp;
                        applicationState = code == 0 ? GisMtEvidenceState.Healthy : GisMtEvidenceState.Error;
                        applicationEvidenceUtc = exchangeTimestamp;
                        if (code == 0)
                        {
                            gisExchangeSuccessUtc = exchangeTimestamp;
                        }
                        else
                        {
                            applicationErrorUtc = exchangeTimestamp;
                            applicationError = $"Ответ ГИС МТ: code {code.ToString(CultureInfo.InvariantCulture)}";
                            applicationErrorKind = GisMtErrorKind.Application;
                        }
                    }
                }

                return new LogEvidence
                {
                    ActualCdn = actual,
                    LogicalCdn = logical,
                    CdnTransportState = transportState,
                    CdnTransportEvidenceUtc = transportEvidenceUtc,
                    LastCdnTransportSuccessUtc = transportSuccessUtc,
                    LastCdnTransportErrorUtc = transportErrorUtc,
                    LastCdnTransportError = transportError,
                    ApplicationExchangeState = applicationState,
                    ApplicationEvidenceUtc = applicationEvidenceUtc,
                    LastSuccessfulGisExchangeUtc = gisExchangeSuccessUtc,
                    LastApplicationErrorUtc = applicationErrorUtc,
                    LastApplicationError = applicationError,
                    ApplicationErrorKind = applicationErrorKind,
                    ControlledChannelState = controlledChannelState,
                    ControlledChannelEvidenceUtc = controlledChannelEvidenceUtc,
                    LastControlledChannelSuccessUtc = controlledChannelSuccessUtc,
                    LastControlledChannelErrorUtc = controlledChannelErrorUtc,
                    LastControlledChannelError = controlledChannelError,
                    LatestTimestampUtc = latest,
                    BytesRead = bytesRead
                };
            }

            private static string TransportErrorSummary(string line)
            {
                if (Contains(line, "no such host")) return "DNS: узел CDN не найден";
                if (Contains(line, "connection refused")) return "Соединение с CDN отклонено";
                if (Contains(line, "deadline exceeded", "client.timeout exceeded")) return "Превышено время ожидания CDN";
                if (Contains(line, "tls")) return "Ошибка TLS-соединения с CDN";
                return SummaryFor(GisMtErrorKind.Connectivity);
            }

            private static string ControlledChannelErrorSummary(string line)
            {
                Match message = Regex.Match(line, @"(?:message:\s*[""']|\[param:\s*)(?<message>[^""'\]]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return message.Success && !string.IsNullOrWhiteSpace(message.Groups["message"].Value)
                    ? message.Groups["message"].Value.Trim()
                    : SummaryFor(GisMtErrorKind.ControlledChannel);
            }

            private static bool IsControlledChannelReady(string line) =>
                Regex.IsMatch(line ?? string.Empty, @"\bcontrolled\s*channel\s*[:=]\s*ready\b|\bcontrolledchannel\s*[:=]\s*ready\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            private static bool IsNewest(DateTimeOffset? candidate, DateTimeOffset? current) =>
                candidate.HasValue && (!current.HasValue || candidate.Value >= current.Value);
        }

        internal static class GisMtSecretRedactor
        {
            private static readonly Regex HeaderRegex = new(
                @"(?im)\b(Authorization\s*:\s*Bearer|X-Fn-Sid\s*[:=]|X-Trnid\s*[:=]|Cookie\s*:|Set-Cookie\s*:|(?:statsAccessToken|licenseToken|refreshToken|accessToken|password)\s*[:=])\s*[^\s,;]+",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            private static readonly Regex JwtRegex = new(
                @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            public static string Redact(string value)
            {
                if (string.IsNullOrEmpty(value)) return value;
                string safe = HeaderRegex.Replace(value, match => match.Groups[1].Value + " [REDACTED]");
                return JwtRegex.Replace(safe, "[REDACTED]");
            }
        }
    }
}
