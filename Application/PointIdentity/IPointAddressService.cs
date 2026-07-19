using HonestFlow.Application.Licensing;

namespace HonestFlow.Application.PointIdentity
{
    public interface IPointAddressService
    {
        PointAddressResult Resolve(LicenseObservationSnapshot snapshot);
        void Save(string deviceId, string address, PointAddressSource source);
    }
}
