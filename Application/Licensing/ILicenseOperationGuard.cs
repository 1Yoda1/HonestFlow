using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseOperationGuard
    {
        void Demand(LicenseOperation operation);
    }
}
