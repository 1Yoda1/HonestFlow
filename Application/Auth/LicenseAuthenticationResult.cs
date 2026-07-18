using HonestFlow.Application.Licensing;
using HonestFlow.Models;

namespace HonestFlow.Application.Auth
{
    public sealed class LicenseAuthenticationResult
    {
        public LicenseAuthenticationResult(
            IPData client,
            LicenseObservationSnapshot licenseSnapshot)
        {
            Client = client;
            LicenseSnapshot = licenseSnapshot;
        }

        public IPData Client { get; }
        public LicenseObservationSnapshot LicenseSnapshot { get; }
    }
}
