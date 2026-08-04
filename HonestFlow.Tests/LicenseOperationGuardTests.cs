using HonestFlow.Application.Licensing;
using HonestFlow.Models.Licensing;
using HonestFlow.Application.Lm;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class LicenseOperationGuardTests
    {
        [Fact]
        public void Demand_AllowsOperationApprovedByPolicy()
        {
            var guard = new LicenseOperationGuard(new StubPolicy(
                new LicenseAccessResult(true, "LICENSE_ALLOWED", string.Empty)));

            guard.Demand(LicenseOperation.InstallComponents);
        }

        [Fact]
        public void Demand_ThrowsWithTechnicalCodeWhenPolicyDenies()
        {
            var guard = new LicenseOperationGuard(new StubPolicy(
                new LicenseAccessResult(false, "LICENSE_CLIENT_CONTEXT_MISMATCH", "denied")));

            LicenseOperationDeniedException exception = Assert.Throws<LicenseOperationDeniedException>(
                () => guard.Demand(LicenseOperation.RestoreLmDatabase));

            Assert.Equal(LicenseOperation.RestoreLmDatabase, exception.Operation);
            Assert.Equal("LICENSE_CLIENT_CONTEXT_MISMATCH", exception.TechnicalCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task LmInitialization_DenialOccursBeforeApiRequest()
        {
            var guard = new LicenseOperationGuard(new StubPolicy(
                new LicenseAccessResult(false, "LICENSE_DEVICE_DISABLED", "denied")));
            var service = new LicensedLmInitializationService(guard);

            LicenseOperationDeniedException exception = await Assert.ThrowsAsync<LicenseOperationDeniedException>(
                () => service.InitializeAsync("secret-token"));

            Assert.Equal(LicenseOperation.InitializeLm, exception.Operation);
        }

        private sealed class StubPolicy : ILicenseAccessPolicy
        {
            private readonly LicenseAccessResult _result;
            public StubPolicy(LicenseAccessResult result) => _result = result;
            public LicenseAccessResult Check(LicenseOperation operation) => _result;
        }
    }
}
