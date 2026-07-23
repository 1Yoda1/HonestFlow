using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class EsmRestStatusClient : IEsmStatusClient, IDisposable
    {
        public const string DefaultSettingsPath = @"C:\ProgramData\ESP\ESM\esm-gui\gui_settings.json";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
        private readonly HttpClient _httpClient;
        private readonly string _settingsPath;
        private readonly bool _ownsClient;

        public EsmRestStatusClient(string settingsPath = DefaultSettingsPath)
            : this(new HttpClient(), settingsPath, true)
        {
        }

        public EsmRestStatusClient(HttpClient httpClient, string settingsPath, bool ownsClient = false)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
            _ownsClient = ownsClient;
        }

        public async Task<EsmStatusResult> GetStatusAsync(CancellationToken cancellationToken)
        {
            int? port = ReadPort();
            if (!port.HasValue)
                return EsmStatusResult.NotConfigured();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            try
            {
                var baseUri = new Uri($"http://127.0.0.1:{port.Value}/api/v1/");
                using var instancesResponse = await _httpClient.GetAsync(
                    new Uri(baseUri, "instances/info"), timeout.Token).ConfigureAwait(false);

                if (instancesResponse.StatusCode == HttpStatusCode.NoContent)
                    return EsmStatusResult.NotConfigured();
                if (!instancesResponse.IsSuccessStatusCode)
                    return EsmStatusResult.Unavailable();

                string instancesJson = await instancesResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                string id = ReadFirstInstanceId(instancesJson);
                if (string.IsNullOrWhiteSpace(id))
                    return EsmStatusResult.NotConfigured();

                string escapedId = Uri.EscapeDataString(id);
                using var statusResponse = await _httpClient.GetAsync(
                    new Uri(baseUri, $"status/{escapedId}"), timeout.Token).ConfigureAwait(false);
                if (statusResponse.StatusCode == HttpStatusCode.NoContent)
                    return EsmStatusResult.NotConfigured();
                if (!statusResponse.IsSuccessStatusCode)
                    return EsmStatusResult.Unavailable();

                string statusJson = await statusResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var status = JsonConvert.DeserializeObject<EsmStatusDto>(statusJson);
                if (status == null)
                    return EsmStatusResult.Unavailable();

                using var lmResponse = await _httpClient.GetAsync(
                    new Uri(baseUri, $"instances/lm/{escapedId}"), timeout.Token).ConfigureAwait(false);
                if (lmResponse.StatusCode == HttpStatusCode.NoContent)
                    return EsmStatusResult.Success(status);
                if (!lmResponse.IsSuccessStatusCode)
                    return EsmStatusResult.Unavailable();

                string lmJson = await lmResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                string normalizedLmJson = lmJson?.Trim();
                status.LmInfo = string.IsNullOrWhiteSpace(normalizedLmJson) ||
                                string.Equals(normalizedLmJson, "null", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(normalizedLmJson, "[]", StringComparison.Ordinal)
                    ? null
                    : JsonConvert.DeserializeObject<EsmLmInfoDto>(normalizedLmJson);
                return EsmStatusResult.Success(status);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return EsmStatusResult.Unavailable();
            }
            catch (HttpRequestException)
            {
                return EsmStatusResult.Unavailable();
            }
            catch (JsonException)
            {
                return EsmStatusResult.Unavailable();
            }
        }

        public async Task<EsmCashRegisterResult> GetCashRegisterStatusAsync(CancellationToken cancellationToken)
        {
            int? port = ReadPort();
            if (!port.HasValue)
                return EsmCashRegisterResult.NotConfigured();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            try
            {
                var uri = new Uri($"http://127.0.0.1:{port.Value}/api/v1/dkktList");
                using var response = await _httpClient.GetAsync(uri, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return EsmCashRegisterResult.Disconnected();
                if (!response.IsSuccessStatusCode)
                    return EsmCashRegisterResult.Unavailable();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                    return EsmCashRegisterResult.Disconnected();

                var snapshot = JsonConvert.DeserializeObject<EsmCashRegisterSnapshotDto>(json);
                if (snapshot == null)
                    return EsmCashRegisterResult.Disconnected();

                // Supports both the diagnostic snapshot { data: { kkt: [...] } }
                // and the native ESM response { kkt: [...] } without retaining KKT identifiers.
                bool hasData = snapshot.Data != null || snapshot.Kkt != null;
                return hasData
                    ? EsmCashRegisterResult.Connected()
                    : EsmCashRegisterResult.Disconnected();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return EsmCashRegisterResult.Unavailable();
            }
            catch (HttpRequestException)
            {
                return EsmCashRegisterResult.Unavailable();
            }
            catch (JsonException)
            {
                return EsmCashRegisterResult.Unavailable();
            }
        }

        public async Task<EsmRegistrationResult> GetRegistrationStatusAsync(CancellationToken cancellationToken)
        {
            int? port = ReadPort();
            if (!port.HasValue)
                return EsmRegistrationResult.NotConfigured();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            try
            {
                var uri = new Uri($"http://127.0.0.1:{port.Value}/api/v1/instances/info");
                using var response = await _httpClient.GetAsync(uri, timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return EsmRegistrationResult.NotConfigured();
                if (!response.IsSuccessStatusCode)
                    return EsmRegistrationResult.Unavailable();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string id = ReadFirstInstanceId(json);
                return string.IsNullOrWhiteSpace(id)
                    ? EsmRegistrationResult.NotConfigured()
                    : EsmRegistrationResult.Registered();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return EsmRegistrationResult.Unavailable();
            }
            catch (HttpRequestException)
            {
                return EsmRegistrationResult.Unavailable();
            }
            catch (JsonException)
            {
                return EsmRegistrationResult.Unavailable();
            }
        }

        private static string ReadFirstInstanceId(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<EsmInstancesDto>(json)
                    ?.Instances?.FirstOrDefault()?.Id;
            }
            catch (JsonSerializationException)
            {
                return JsonConvert.DeserializeObject<EsmInstanceDto[]>(json)
                    ?.FirstOrDefault()?.Id;
            }
        }

        private int? ReadPort()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return null;

                var settings = JsonConvert.DeserializeObject<EsmGuiSettings>(File.ReadAllText(_settingsPath));
                return settings?.Port is > 0 and <= 65535 ? settings.Port : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (JsonException) { return null; }
        }

        public void Dispose()
        {
            if (_ownsClient)
                _httpClient.Dispose();
        }

        private sealed class EsmGuiSettings
        {
            [JsonProperty("port")]
            public int? Port { get; set; }
        }

        private sealed class EsmInstancesDto
        {
            [JsonProperty("instances")]
            public EsmInstanceDto[] Instances { get; set; }
        }

        private sealed class EsmInstanceDto
        {
            [JsonProperty("id")]
            public string Id { get; set; }
        }

        private sealed class EsmCashRegisterSnapshotDto
        {
            [JsonProperty("data")]
            public EsmCashRegisterDataDto Data { get; set; }

            [JsonProperty("kkt")]
            public EsmCashRegisterMarkerDto[] Kkt { get; set; }
        }

        private sealed class EsmCashRegisterDataDto
        {
            [JsonProperty("kkt")]
            public EsmCashRegisterMarkerDto[] Kkt { get; set; }
        }

        private sealed class EsmCashRegisterMarkerDto
        {
            // Deliberately empty: presence of Data is the only permitted signal.
            // Serial numbers, INN and fiscal identifiers are never retained.
        }
    }
}
