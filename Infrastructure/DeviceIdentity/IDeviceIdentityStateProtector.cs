namespace HonestFlow.Infrastructure.DeviceIdentity
{
    public interface IDeviceIdentityStateProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] protectedData);
    }
}
