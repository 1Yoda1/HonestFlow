using System;
using System.Collections.Generic;

namespace HonestFlow.Infrastructure.Updates;

public static class ReleaseTrustStore
{
    public const bool RequireSignedManifests = true;

    private static readonly IReadOnlyDictionary<string, byte[]> Keys =
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["release_6bd64214975e48a3af2c20b8"] = Convert.FromBase64String(
                "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErrg2ECb/9kfRZWC8ORKsbzCKlwcU8SIPCt7fGmF4ZwmLBeKqlyNsDXC7wE8GhZO1fRDXiwDwHpuyoOORYN4E7g==")
        };

    public static ReleaseManifestVerifier CreateVerifier() => new(Keys);
}
