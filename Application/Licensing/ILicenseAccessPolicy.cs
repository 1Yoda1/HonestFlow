using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseAccessPolicy
    {
        LicenseAccessResult Check(LicenseFeature feature);
    }
}
