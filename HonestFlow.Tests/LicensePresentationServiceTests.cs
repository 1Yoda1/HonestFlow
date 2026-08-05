using System;
using HonestFlow.Application.Licensing;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicensePresentationServiceTests
    {
        private readonly LicensePresentationService _service = new();

        [Fact]
        public void Create_Allowed_ReturnsNonModalStatus()
        {
            LicenseDecisionPresentation result = _service.Create(new LicenseObservationSnapshot
            {
                Decision = LicenseDecision.Allowed
            });

            Assert.False(result.IsWarning);
            Assert.Contains("Лицензия проверена", result.Message);
        }

        [Fact]
        public void Create_VersionTooOld_IncludesMinimumVersion()
        {
            LicenseDecisionPresentation result = _service.Create(new LicenseObservationSnapshot
            {
                Decision = LicenseDecision.VersionTooOld,
                MinimumRequiredVersion = new Version(3, 1, 0)
            });

            Assert.True(result.IsWarning);
            Assert.Contains("3.1.0", result.Message);
        }

        [Fact]
        public void IsForClient_RequiresExactClientId()
        {
            var client = new IPData { ClientId = "client-a" };

            Assert.True(_service.IsForClient(client, new LicenseObservationSnapshot { ClientId = "client-a" }));
            Assert.False(_service.IsForClient(client, new LicenseObservationSnapshot { ClientId = "client-b" }));
        }
    }
}
