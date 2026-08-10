namespace HonestFlow.Infrastructure.Api
{
    public interface IApiSessionProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] protectedData);
    }
}
