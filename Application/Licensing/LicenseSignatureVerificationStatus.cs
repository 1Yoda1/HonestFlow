namespace HonestFlow.Application.Licensing
{
    public enum LicenseSignatureVerificationStatus
    {
        Valid,
        InvalidSignature,
        UnknownKeyId,
        InvalidPublicKey,
        InvalidSignatureFile
    }
}
