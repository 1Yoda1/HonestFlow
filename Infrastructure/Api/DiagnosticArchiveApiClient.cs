using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class DiagnosticArchiveApiClient
    {
        private static readonly HttpClient ObjectStorageClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        private readonly IApiSessionService _session;

        public DiagnosticArchiveApiClient(IApiSessionService session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public async Task<DiagnosticUploadResult> UploadAsync(
            string archivePath,
            string honestFlowVersion,
            Action<int, string> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                throw new FileNotFoundException("Диагностический архив не найден.", archivePath);

            var file = new FileInfo(archivePath);
            progress?.Invoke(10, "Проверяем архив");
            string sha256;
            await using (FileStream hashStream = File.OpenRead(archivePath))
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));

            var createPayload = new
            {
                fileName = file.Name,
                sizeBytes = file.Length,
                sha256,
                honestFlowVersion
            };
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "api/support/diagnostics/uploads")
            {
                Content = JsonContent(createPayload)
            };
            using HttpResponseMessage createResponse = await _session.SendAuthorizedAsync(createRequest, cancellationToken);
            await EnsureSuccessAsync(createResponse, "Не удалось создать загрузку диагностики.", cancellationToken);
            DiagnosticUploadTicket ticket = JsonConvert.DeserializeObject<DiagnosticUploadTicket>(
                await createResponse.Content.ReadAsStringAsync(cancellationToken))
                ?? throw new InvalidOperationException("Сервер вернул пустой ответ для загрузки диагностики.");

            progress?.Invoke(30, "Загружаем архив в защищённое хранилище");
            await using FileStream uploadStream = File.OpenRead(archivePath);
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, ticket.UploadUrl)
            {
                Content = new StreamContent(uploadStream)
            };
            uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            uploadRequest.Content.Headers.ContentLength = file.Length;
            using HttpResponseMessage uploadResponse = await ObjectStorageClient.SendAsync(
                uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(uploadResponse, "Cloud.ru отклонил загрузку архива.", cancellationToken);

            progress?.Invoke(90, "Подтверждаем загрузку");
            using var completeRequest = new HttpRequestMessage(
                HttpMethod.Post, $"api/support/diagnostics/{ticket.Id}/complete")
            {
                Content = JsonContent(new { sizeBytes = file.Length, sha256 })
            };
            using HttpResponseMessage completeResponse = await _session.SendAuthorizedAsync(completeRequest, cancellationToken);
            await EnsureSuccessAsync(completeResponse, "Не удалось подтвердить загрузку диагностики.", cancellationToken);
            progress?.Invoke(100, "Диагностика отправлена");
            return new DiagnosticUploadResult(ticket.Id, ticket.ArchiveExpiresAtUtc);
        }

        private static StringContent JsonContent(object value) =>
            new(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");

        private static async Task EnsureSuccessAsync(
            HttpResponseMessage response, string message, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode) return;
            string details = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"{message} HTTP {(int)response.StatusCode}. {details}".Trim());
        }

        private sealed class DiagnosticUploadTicket
        {
            public int Id { get; set; }
            public string UploadUrl { get; set; }
            public DateTime ArchiveExpiresAtUtc { get; set; }
        }
    }

    public sealed record DiagnosticUploadResult(int Id, DateTime ArchiveExpiresAtUtc);
}
