using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class GisMtDiagnosticProbeTests
    {
        [Fact]
        public async Task HealthyRest_WithConfiguredAndAvailableCdn_IsHealthy()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteCache(blocked: false);

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(0), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Healthy, result.State);
            Assert.Equal("Работает", result.Summary);
            Assert.Equal(2, result.ConfiguredCdnCount);
            Assert.Equal(1, result.AvailableCdnCount);
        }

        [Fact]
        public async Task FreshConnectivityError_IsErrorWithCdnSummary()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.Timestamp + " ERR Нет подключения к CDN площадке");

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Error, result.State);
            Assert.Equal(GisMtEvidenceState.Error, result.CdnTransportState);
            Assert.NotNull(result.LastCdnTransportErrorUtc);
            Assert.Equal("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task RegistrationError_IsNotClassifiedAsTransportFailure()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.Timestamp + " ERR Ошибка регистрации в ГИС МТ");

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Error, result.State);
            Assert.Equal(GisMtEvidenceState.Error, result.ApplicationExchangeState);
            Assert.Equal(GisMtErrorKind.Registration, result.LastApplicationErrorKind);
            Assert.Equal("Ошибка регистрации в ГИС МТ", result.Summary);
        }

        [Fact]
        public async Task AllCachedCdnBlocked_ReportsNoAvailableSites()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteCache(blocked: true);

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Error, result.State);
            Assert.Equal("Нет доступных CDN-площадок", result.Summary);
            Assert.Equal(1, result.BlockedCdnCount);
        }

        [Fact]
        public async Task MissingConfig_ReturnsErrorWithoutException()
        {
            using var fixture = new Fixture();

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(0), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Error, result.State);
            Assert.Equal("Не получена конфигурация CDN", result.Summary);
        }

        [Fact]
        public async Task InsufficientEvidence_ReturnsUnknown()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Unknown, result.State);
            Assert.Equal("Не удалось проверить", result.Summary);
        }

        [Fact]
        public async Task CredentialsInLog_AreNeverReturnedByDiagnostics()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.Timestamp + " ERR Нет подключения к CDN площадке",
                "Authorization: Bearer TEST_REDACT_ME",
                "Authorization: Bearer eyJTESTTOKEN1.eyJTESTTOKEN2.eyJTESTTOKEN3",
                "X-Fn-Sid: TEST_SID",
                "X-Trnid: TEST_TRNID"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);
            string output = result.Details + Environment.NewLine + string.Join(Environment.NewLine, result.Evidence);

            Assert.DoesNotContain("TEST_REDACT_ME", output);
            Assert.DoesNotContain("TEST_SID", output);
            Assert.DoesNotContain("TEST_TRNID", output);
            Assert.DoesNotContain("eyJTESTTOKEN", output);
        }

        [Fact]
        public async Task LargeLog_ReadsOnlyConfiguredBoundedTail()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(new string('x', 100_000) + Environment.NewLine +
                fixture.Timestamp + " ERR Нет подключения к CDN площадке");

            GisMtDiagnosticResult result = await fixture.Probe(logTailBytes: 4096)
                .CheckAsync(Rest(null), CancellationToken.None);

            Assert.InRange(result.LogBytesRead, 1, 4096);
            Assert.Equal(GisMtDiagnosticState.Error, result.State);
        }

        [Fact]
        public async Task ExclusivelyLockedLog_IsHandledGracefully()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.Timestamp + " ERR Нет подключения к CDN площадке");
            using var locked = new FileStream(fixture.LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Unknown, result.State);
            Assert.Equal(0, result.LogBytesRead);
        }

        [Fact]
        public async Task StrongLogicalAndActualCdnMismatch_AddsEvidence()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.Timestamp + " INF Устанавливаем адрес cdn01.crpt.ru:19101 для CDN площадки",
                "URL: https://cdn02.crpt.ru:19101/api/v4/cdn/health/check",
                "Status: 200 OK",
                "{ \"code\": 0 }"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.True(result.PossibleCdnStateMismatch);
            Assert.Contains("Возможна ошибка переключения CDN", result.Evidence);
            Assert.Equal("cdn01.crpt.ru:19101", result.LastLogicalCdn);
            Assert.Equal("cdn02.crpt.ru:19101", result.LastActualCdn);
            Assert.NotNull(result.LastSuccessfulGisExchangeUtc);
        }

        [Fact]
        public async Task WeakHostnameMention_DoesNotCreateMismatch()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.Timestamp + " DBG Возможный адрес cdn01.crpt.ru:19101",
                "URL: https://cdn02.crpt.ru:19101/api/v4/cdn/health/check",
                "Status: 200 OK",
                "{ \"code\": 0 }"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.False(result.PossibleCdnStateMismatch);
            Assert.Null(result.LastLogicalCdn);
        }

        [Fact]
        public async Task AuthHttp200Challenge_IsNotTreatedAsSuccessfulGisInteraction()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.Timestamp + " DBG",
                "URL: https://cdn02.crpt.ru:19101/api/v4/auth",
                "Status: 200 OK",
                "{ \"challenge\": \"synthetic\" }"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Unknown, result.State);
            Assert.Equal(GisMtEvidenceState.Healthy, result.CdnTransportState);
            Assert.NotNull(result.LastCdnTransportSuccessUtc);
            Assert.Null(result.LastSuccessfulGisExchangeUtc);
        }

        [Fact]
        public async Task ControlledChannelError_IsSeparateAndDoesNotOverrideHealthyRestState()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.Timestamp + " ERR controlled channel failed at synthetic DKKT stage");

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(0), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Healthy, result.State);
            Assert.Equal("Работает", result.Summary);
            Assert.Equal(GisMtEvidenceState.Error, result.ControlledChannelState);
            Assert.Contains("Ошибка защищённого канала", result.ControlledChannelDetails);
        }

        [Fact]
        public async Task FreshControlledChannelReady_IsThePrimaryPositiveEvidence()
        {
            using var fixture = new Fixture();
            fixture.WriteLog(fixture.Timestamp + " INF ControlledChannel = ready");

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(7), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Healthy, result.State);
            Assert.Equal("Работает", result.Summary);
            Assert.Equal(GisMtEvidenceState.Healthy, result.ControlledChannelState);
            Assert.Equal(fixture.Now, result.LastControlledChannelSuccessUtc);
        }

        [Fact]
        public async Task NewerApplicationFailure_OverridesEarlierControlledChannelReady()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.At(-5, "INF ControlledChannel = ready"),
                fixture.At(-4, "ERR Ошибка регистрации в ГИС МТ")));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Error, result.State);
            Assert.Equal("Ошибка регистрации в ГИС МТ", result.Summary);
        }

        [Fact]
        public async Task OlderTransportFailure_IsSupersededByNewerAuthHttpResponse()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.At(-10, "ERR Post \"https://cdn-test.example:19101/api/v4/auth\": context deadline exceeded"),
                fixture.At(-5, "DBG"),
                "URL: https://cdn-test.example:19101/api/v4/auth",
                "Status: 200 OK",
                "{ \"status\": 0 }"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtEvidenceState.Healthy, result.CdnTransportState);
            Assert.Equal(fixture.UtcAt(-5), result.LastCdnTransportSuccessUtc);
            Assert.NotEqual("Нет связи с CDN", result.Summary);
            Assert.DoesNotContain("Последняя ошибка транспорта CDN", result.Evidence);
        }

        [Fact]
        public async Task AuthHttp200FollowedByClosedShift_IsHealthyTransportAndControlledChannelError()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.At(-5, "DBG"),
                "URL: https://cdn-test.example:19101/api/v4/auth",
                "Status: 200 OK",
                "{ \"status\": 0 }",
                fixture.At(-4, "INF EncryptData: операция шифрования (CONTROLLED)"),
                fixture.At(-4, "INF Ошибка шифрования, код: code:73 message:\"Смена закрыта - операция невозможна\" module:\"ecr\""),
                fixture.At(-4, "ERR Ошибка установки контролируемого канала: error 2021"),
                fixture.At(-3, "ERR error 2030: Ошибка шифрования в ФН [param: Смена закрыта - операция невозможна]")));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(0), CancellationToken.None);

            Assert.Equal(GisMtDiagnosticState.Healthy, result.State);
            Assert.Equal("Работает", result.Summary);
            Assert.Equal(GisMtEvidenceState.Healthy, result.CdnTransportState);
            Assert.Equal(GisMtEvidenceState.Error, result.ControlledChannelState);
            Assert.Contains("Смена закрыта - операция невозможна", result.LastControlledChannelError);
            Assert.NotEqual("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task NewerTransportFailure_OverridesOlderHttpSuccess()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.At(-10, "DBG"),
                "URL: https://cdn-test.example:19101/api/v4/auth",
                "Status: 200 OK",
                "{ \"status\": 0 }",
                fixture.At(-5, "ERR Post \"https://cdn-test.example:19101/api/v4/auth\": context deadline exceeded")));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtEvidenceState.Error, result.CdnTransportState);
            Assert.Equal(fixture.UtcAt(-5), result.LastCdnTransportErrorUtc);
            Assert.Equal(GisMtDiagnosticState.Error, result.State);
            Assert.Equal("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task Http403_IsReachableTransportAndSeparateApplicationError()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(string.Join(Environment.NewLine,
                fixture.At(-5, "DBG"),
                "URL: https://cdn-test.example:19101/api/v4/cdn/health/check",
                "Status: 403 Forbidden"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtEvidenceState.Healthy, result.CdnTransportState);
            Assert.Equal(GisMtEvidenceState.Error, result.ApplicationExchangeState);
            Assert.Equal("HTTP 403", result.LastApplicationError);
            Assert.Equal("Ошибка обмена с ГИС МТ", result.Summary);
            Assert.NotEqual("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task MixedUnavailableLine_WithFnCryptoRootCause_IsControlledChannelOnly()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.At(-5,
                "WRN Площадка https://cdn-test.example:19101 недоступна (latency/KK): не удалось установить контролируемый канал: error 2030: Ошибка шифрования в ФН"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtEvidenceState.Unknown, result.CdnTransportState);
            Assert.Equal(GisMtEvidenceState.Error, result.ControlledChannelState);
            Assert.NotEqual("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task MixedUnavailableLine_WithNetworkRootCause_IsTransportFailureOnly()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.At(-5,
                "WRN Площадка https://cdn-test.example:19101 недоступна (latency/KK): не удалось установить контролируемый канал: error 2016 context deadline exceeded"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtEvidenceState.Error, result.CdnTransportState);
            Assert.Equal(GisMtEvidenceState.Unknown, result.ControlledChannelState);
            Assert.Equal("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task UnrelatedServiceNetworkFailure_DoesNotBecomeCdnTransportFailure()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteLog(fixture.At(-5,
                "ERR Post \"https://telemetry.example/api/events\": context deadline exceeded"));

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(null), CancellationToken.None);

            Assert.Equal(GisMtEvidenceState.Unknown, result.CdnTransportState);
            Assert.NotEqual("Нет связи с CDN", result.Summary);
        }

        [Fact]
        public async Task MissingCacheTimestamp_RemainsUnknownAndNeverShowsYearOne()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteCacheWithTimestamp("0001-01-01T00:00:00+00:00");

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(0), CancellationToken.None);

            Assert.Null(result.CacheLastCheckedUtc);
            Assert.Null(result.CacheIsFresh);
            Assert.Contains("Последняя проверка CDN cache: нет данных", result.Evidence);
            Assert.Contains("Актуальность CDN cache: неизвестна", result.Evidence);
            Assert.DoesNotContain("0001-", result.Details);
        }

        [Fact]
        public async Task UnblockedCacheEntry_IsPresentedAsNotBlocked_NotNetworkAvailable()
        {
            using var fixture = new Fixture();
            fixture.WriteConfig();
            fixture.WriteCache(blocked: false);

            GisMtDiagnosticResult result = await fixture.Probe().CheckAsync(Rest(0), CancellationToken.None);

            Assert.Contains("Не заблокировано по CDN cache: 1", result.Evidence);
            Assert.DoesNotContain(result.Evidence, value => value.StartsWith("Доступно:", StringComparison.Ordinal));
        }

        private static EsmStatusResult Rest(int? code) => EsmStatusResult.Success(
            new EsmStatusDto { Gismt = code.HasValue ? new EsmComponentStatus { Code = code } : null },
            51077,
            "test-instance");

        private sealed class Fixture : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "honestflow-gismt-" + Guid.NewGuid().ToString("N"));
            public readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

            public Fixture()
            {
                Directory.CreateDirectory(_root);
                Directory.CreateDirectory(Path.Combine(_root, "cache"));
                Directory.CreateDirectory(Path.Combine(_root, "log"));
            }

            public string Timestamp => Now.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss.fff");
            public string At(int minutesFromNow, string message) =>
                Now.AddMinutes(minutesFromNow).ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss.fff") + " " + message;
            public DateTimeOffset UtcAt(int minutesFromNow) => Now.AddMinutes(minutesFromNow);
            public string LogPath => Path.Combine(_root, "log", "esm-cm_test-instance.log");

            public GisMtDiagnosticProbe Probe(int logTailBytes = GisMtDiagnosticProbe.DefaultLogTailBytes) =>
                new(_root, () => Now, logTailBytes);

            public void WriteConfig() => File.WriteAllText(
                Path.Combine(_root, "config_test-instance.yml"),
                "gisMT:\n" +
                "  url: https://ts-reg.crpt.ru:19100\n" +
                "  cdn:\n" +
                "    - url: https://cdn01.crpt.ru:19101\n" +
                "    - url: https://cdn02.crpt.ru:19101\n");

            public void WriteCache(bool blocked) => File.WriteAllText(
                Path.Combine(_root, "cache", "test-instance_cdn_cache.json"),
                "[{\"host\":\"cdn01.crpt.ru:19101\",\"latency_ms\":18," +
                $"\"block\":{blocked.ToString().ToLowerInvariant()}," +
                $"\"last_checked_utc\":\"{Now.AddMinutes(-1):O}\"}}]");

            public void WriteCacheWithTimestamp(string timestamp) => File.WriteAllText(
                Path.Combine(_root, "cache", "test-instance_cdn_cache.json"),
                "[{\"host\":\"cdn-test.example:19101\",\"latency_ms\":18,\"block\":false," +
                $"\"last_checked_utc\":\"{timestamp}\"}}]");

            public void WriteLog(string content) => File.WriteAllText(LogPath, content, new UTF8Encoding(false));

            public void Dispose()
            {
                try { Directory.Delete(_root, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
