using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.Installation;
using HonestFlow.Application.PointStatus;
using HonestFlow.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class EsmTsPiotRegistrationClient : IEsmTsPiotRegistrationClient, IDisposable
    {
        private static readonly TimeSpan KktDiscoveryTimeout = TimeSpan.FromSeconds(3);
        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly IEsmStatusClient _registrationStatusClient;
        private readonly IEsmCmServiceRegistrationWaiter _serviceWaiter;
        private readonly string _settingsPath;
        private readonly bool _ownsClient;

        public EsmTsPiotRegistrationClient(string settingsPath = EsmRestStatusClient.DefaultSettingsPath)
            : this(
                SharedHttpClient,
                settingsPath,
                serviceWaiter: new EsmCmServiceRegistrationWaiter(),
                registrationStatusClient: new EsmRestStatusClient(settingsPath))
        {
        }

        public EsmTsPiotRegistrationClient(
            HttpClient httpClient,
            string settingsPath,
            bool ownsClient = false,
            IEsmCmServiceRegistrationWaiter serviceWaiter = null,
            IEsmStatusClient registrationStatusClient = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
            _ownsClient = ownsClient;
            _serviceWaiter = serviceWaiter ?? new EsmCmServiceRegistrationWaiter();
            _registrationStatusClient = registrationStatusClient ?? new EsmRestStatusClient(settingsPath);
        }

        public async Task<TsPiotRegistrationResult> RegisterAsync(CancellationToken cancellationToken)
        {
            int port = EsmLocalApiSettings.ResolvePort(_settingsPath);
            var baseUri = new Uri($"http://127.0.0.1:{port}/api/v1/");

            try
            {
                using var discoveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                discoveryTimeout.CancelAfter(KktDiscoveryTimeout);
                using var kktResponse = await _httpClient.GetAsync(new Uri(baseUri, "dkktList"), discoveryTimeout.Token)
                    .ConfigureAwait(false);
                if (!kktResponse.IsSuccessStatusCode)
                    return TsPiotRegistrationResult.EsmUnavailable($"dkktList_http_{(int)kktResponse.StatusCode}");

                string kktJson = await kktResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                string kktSerial = ReadFirstKktSerial(kktJson);
                if (string.IsNullOrWhiteSpace(kktSerial))
                    return TsPiotRegistrationResult.KktNotDetected("kkt_serial_missing");

                string body = JsonConvert.SerializeObject(new { id = kktSerial });
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "tspiot"))
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                using var registrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<HttpResponseMessage> registrationRequest = _httpClient.SendAsync(request, registrationCancellation.Token);
                Task<bool> serviceAppeared = _serviceWaiter.WaitForServiceAsync(kktSerial, cancellationToken);
                Task completed = await Task.WhenAny(registrationRequest, serviceAppeared).ConfigureAwait(false);

                if (completed == serviceAppeared)
                {
                    bool appeared = await serviceAppeared.ConfigureAwait(false);
                    if (appeared)
                    {
                        registrationCancellation.Cancel();
                        await ObserveCanceledRequestAsync(registrationRequest).ConfigureAwait(false);
                        return await ConfirmRegistrationAsync(cancellationToken).ConfigureAwait(false);
                    }

                    registrationCancellation.Cancel();
                    await ObserveCanceledRequestAsync(registrationRequest).ConfigureAwait(false);
                    return TsPiotRegistrationResult.RegistrationFailed("esm_cm_service_timeout");
                }

                using HttpResponseMessage registrationResponse = await registrationRequest.ConfigureAwait(false);
                if (!registrationResponse.IsSuccessStatusCode)
                    return TsPiotRegistrationResult.RegistrationFailed($"tspiot_http_{(int)registrationResponse.StatusCode}");

                if (!await serviceAppeared.ConfigureAwait(false))
                    return TsPiotRegistrationResult.RegistrationFailed("esm_cm_service_timeout");

                return await ConfirmRegistrationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return TsPiotRegistrationResult.EsmUnavailable("timeout");
            }
            catch (HttpRequestException)
            {
                return TsPiotRegistrationResult.EsmUnavailable("request_failed");
            }
            catch (JsonException)
            {
                return TsPiotRegistrationResult.RegistrationFailed("dkktList_invalid_json");
            }
        }

        public void Dispose()
        {
            if (_ownsClient)
                _httpClient.Dispose();
        }

        private static string ReadFirstKktSerial(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            JObject root = JObject.Parse(json);
            JArray kkt = root.SelectToken("data.kkt") as JArray ?? root["kkt"] as JArray;
            return kkt?.FirstOrDefault()?["kktSerial"]?.Value<string>()?.Trim();
        }

        private async Task<TsPiotRegistrationResult> ConfirmRegistrationAsync(CancellationToken cancellationToken)
        {
            EsmRegistrationResult registration = await _registrationStatusClient
                .GetRegistrationStatusAsync(cancellationToken)
                .ConfigureAwait(false);

            return registration.Kind == EsmRegistrationResultKind.Registered
                ? TsPiotRegistrationResult.Success()
                : TsPiotRegistrationResult.RegistrationFailed("registration_not_confirmed");
        }

        private static async Task ObserveCanceledRequestAsync(Task<HttpResponseMessage> request)
        {
            try
            {
                using HttpResponseMessage response = await request.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}
