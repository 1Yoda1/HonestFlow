using System;

namespace HonestFlow.Application.Licensing
{
    public interface ITrustedLicenseClock
    {
        DateTimeOffset UtcNow { get; }

        void ObserveTrustedTime(
            DateTimeOffset signedIssuedAtUtc,
            DateTimeOffset signedValidUntilUtc,
            DateTimeOffset? httpsDateUtc);
    }
}
