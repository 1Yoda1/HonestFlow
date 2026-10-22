using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api;

public sealed class RemoteAccessApiClient
{
    private readonly IApiSessionService _session;

    public RemoteAccessApiClient(IApiSessionService session) =>
        _session = session ?? throw new ArgumentNullException(nameof(session));

    public async Task<RemoteAccessKeyDto> GetTrustedKeyAsync(CancellationToken token)
    {
        RemoteAccessKeyDto key = await GetAsync<RemoteAccessKeyDto>("api/support/remote-access/key", token);
        if (key == null || !EmbeddedLicenseTrust.CreateKeyRegistry()
                .TryGetValue(key.SigningKeyId ?? "", out string publicSigningKey))
            throw new CryptographicException("Ключ шифрования HonestDesk не подписан доверенным ключом.");
        byte[] signedBytes = Encoding.UTF8.GetBytes(
            $"HonestFlow.RemoteAccessKey.v1\n{key.KeyId}\n{key.PublicKeyBase64}");
        using var signer = ECDsa.Create();
        signer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicSigningKey), out _);
        if (!signer.VerifyData(signedBytes, Convert.FromBase64String(key.SignatureBase64),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new CryptographicException("Подпись ключа HonestDesk неверна.");
        using var rsa = RSA.Create();
        byte[] publicKey = Convert.FromBase64String(key.PublicKeyBase64);
        rsa.ImportSubjectPublicKeyInfo(publicKey, out int read);
        if (read != publicKey.Length || rsa.KeySize != 3072)
            throw new CryptographicException("Некорректный ключ шифрования HonestDesk.");
        return key;
    }

    public static string EncryptPassword(RemoteAccessKeyDto key, string password)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKeyBase64), out _);
        return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(password),
            RSAEncryptionPadding.OaepSHA256));
    }

    public Task<RemoteAccessCredentialStatusDto> GetStatusAsync(CancellationToken token) =>
        GetAsync<RemoteAccessCredentialStatusDto>("api/support/remote-access/current", token);

    public Task PutCredentialAsync(string credentialId, string keyId, string ruDesktopId,
        string ciphertextBase64, CancellationToken token) =>
        SendAsync(HttpMethod.Put, "api/support/remote-access/current",
            new { credentialId, keyId, ruDesktopId, ciphertextBase64 }, token);

    public Task RevokeAsync(CancellationToken token) =>
        SendAsync(HttpMethod.Delete, "api/support/remote-access/current", null, token);

    public async Task<SupportRequestAcceptedDto> RequestHelpAsync(string credentialId, string subject,
        string message, string contact, string honestFlowVersion, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/support/remote-requests")
        {
            Content = new StringContent(JsonConvert.SerializeObject(
                new { credentialId, subject, message, contact, honestFlowVersion }),
                Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, token);
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<SupportRequestAcceptedDto>(
                   await response.Content.ReadAsStringAsync(token))
               ?? throw new InvalidOperationException("Сервер не вернул созданную заявку помощи.");
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, token);
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(token));
    }

    private async Task SendAsync(HttpMethod method, string path, object body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body),
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _session.SendAuthorizedAsync(request, token);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class RemoteAccessKeyDto
{
    public string KeyId { get; set; }
    public string PublicKeyBase64 { get; set; }
    public string SignatureBase64 { get; set; }
    public string SigningKeyId { get; set; }
}

public sealed class RemoteAccessCredentialStatusDto
{
    public bool IsActive { get; set; }
    public string CredentialId { get; set; }
    public string RuDesktopId { get; set; }
    public string KeyId { get; set; }
    public int? ActiveRequestId { get; set; }
    public DateTimeOffset? ActiveRequestCreatedAtUtc { get; set; }
}

public sealed class SupportRequestAcceptedDto
{
    public int Id { get; set; }
    public string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
