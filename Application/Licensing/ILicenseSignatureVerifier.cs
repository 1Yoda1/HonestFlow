using System;

namespace HonestFlow.Application.Licensing
{
    public interface ILicenseSignatureVerifier
    {
        LicenseSignatureVerificationResult Verify(
            ReadOnlyMemory<byte> manifestBytes,
            ReadOnlyMemory<byte> signatureFileBytes);
    }
}
