using System;
using System.Security.Cryptography;
using System.Text;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class DpapiLicenseCacheMetadataProtector : ILicenseCacheMetadataProtector
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.LicenseCache.Metadata.v1");

        public byte[] Protect(byte[] plaintext)
        {
            if (plaintext == null)
                throw new ArgumentNullException(nameof(plaintext));

            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            if (protectedData == null)
                throw new ArgumentNullException(nameof(protectedData));

            return ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.LocalMachine);
        }
    }
}
