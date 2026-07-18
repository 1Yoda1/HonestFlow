using System;
using System.Security.Cryptography;
using System.Text;

namespace HonestFlow.Infrastructure.DeviceIdentity
{
    public sealed class DpapiDeviceIdentityStateProtector : IDeviceIdentityStateProtector
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("HonestFlow.DeviceIdentity.State.v1");

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
