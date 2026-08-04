using System;
using System.Threading.Tasks;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Api;
using HonestFlow.Models.Licensing;
using HonestFlow.Models;
using HonestFlow.Infrastructure;

namespace HonestFlow.Application.Lm
{
    public sealed class LicensedLmInitializationService
    {
        private readonly ILicenseOperationGuard _licenseGuard;

        public LicensedLmInitializationService(ILicenseOperationGuard licenseGuard)
        {
            _licenseGuard = licenseGuard ?? throw new ArgumentNullException(nameof(licenseGuard));
        }

        public async Task<ApiSimpleResponse> InitializeAsync(string token)
        {
            using var audit = Logger.BeginOperation("Инициализация ЛМ ЧЗ", nameof(LicensedLmInitializationService));
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("LM token is required.", nameof(token));

            _licenseGuard.Demand(LicenseOperation.InitializeLm);
            using var api = new LmApiClient(enableDetailedLogging: false);
            return await api.InitializeFull(token);
        }
    }
}
