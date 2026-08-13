using System;

namespace HonestFlow.LicenseSigning
{
    public interface ILicenseSignatureFileCreator
    {
        byte[] CreateSignatureFile(ReadOnlyMemory<byte> manifestBytes, string keyId, string privateKeyPkcs8Pem);
    }
}
