using System;
using System.Security.Cryptography;
using System.Text;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class DpapiApiSessionProtector : IApiSessionProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HonestFlow.ApiSession.v1");

        public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(
            plaintext ?? throw new ArgumentNullException(nameof(plaintext)),
            Entropy,
            DataProtectionScope.LocalMachine);

        public byte[] Unprotect(byte[] protectedData) => ProtectedData.Unprotect(
            protectedData ?? throw new ArgumentNullException(nameof(protectedData)),
            Entropy,
            DataProtectionScope.LocalMachine);
    }
}
