using System;
using System.Collections.Generic;

namespace HonestFlow.Infrastructure.Licensing
{
    public sealed class LicensePublicKeyRegistry
    {
        private readonly IReadOnlyDictionary<string, string> _publicKeys;

        public LicensePublicKeyRegistry(IReadOnlyDictionary<string, string> publicKeys)
        {
            _publicKeys = publicKeys ?? throw new ArgumentNullException(nameof(publicKeys));
        }

        public bool TryGetSubjectPublicKeyInfo(string keyId, out string base64PublicKey)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                base64PublicKey = null;
                return false;
            }

            return _publicKeys.TryGetValue(keyId, out base64PublicKey);
        }
    }
}
