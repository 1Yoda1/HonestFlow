using System;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests;

public sealed class RemoteAccessEncryptionTests
{
    [Fact]
    public void GeneratedDevicePassword_IsOnlyRecoverableWithHonestDeskPrivateKey()
    {
        using var key = RSA.Create(3072);
        var published = new RemoteAccessKeyDto
        {
            KeyId = Guid.NewGuid().ToString("D"),
            PublicKeyBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
        };
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        string ciphertext = RemoteAccessApiClient.EncryptPassword(published, password);

        Assert.False(ciphertext.Contains(password, StringComparison.Ordinal));
        Assert.Equal(password, Encoding.UTF8.GetString(key.Decrypt(
            Convert.FromBase64String(ciphertext), RSAEncryptionPadding.OaepSHA256)));
    }
}
