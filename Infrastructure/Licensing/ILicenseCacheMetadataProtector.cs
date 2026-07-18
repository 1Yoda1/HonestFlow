namespace HonestFlow.Infrastructure.Licensing
{
    public interface ILicenseCacheMetadataProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] protectedData);
    }
}
