using System;
using System.Collections.Generic;

namespace HonestFlow.Infrastructure.Licensing
{
    public static class EmbeddedLicenseTrust
    {
        public const string ProductionKeyId = "production-2026-01";
        public const string Primary2026KeyId = "primary-2026";

        // Public ECDSA P-256 SubjectPublicKeyInfo. The private key is not part of HonestFlow.
        public const string ProductionPublicKeySubjectPublicKeyInfoBase64 =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE6tH6YTjBpp89XzotKfiwXlqKKSRCrh+LEYTVfGYcrM+rObTnaBZ73aKcEZEQJkcZYLuBrToxutqmlYUQL6mrZw==";

        public static Dictionary<string, string> CreateKeyRegistry()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProductionKeyId] = ProductionPublicKeySubjectPublicKeyInfoBase64,
                [Primary2026KeyId] = ProductionPublicKeySubjectPublicKeyInfoBase64
            };
        }
    }
}
