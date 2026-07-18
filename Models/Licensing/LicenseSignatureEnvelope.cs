namespace HonestFlow.Models.Licensing
{
    public sealed class LicenseSignatureEnvelope
    {
        public const string EcdsaP256Sha256Algorithm = "ECDSA_P256_SHA256";

        public string KeyId { get; set; }
        public string Algorithm { get; set; }
        public string Signature { get; set; }
    }
}
