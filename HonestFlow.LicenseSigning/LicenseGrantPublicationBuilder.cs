using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Licensing;
using HonestFlow.Models.Licensing;
using Newtonsoft.Json;

namespace HonestFlow.LicenseSigning
{
    /// <summary>
    /// Converts the private release-side directory into separately signed,
    /// non-enumerating per-device publication artifacts.
    /// </summary>
    public sealed class LicenseGrantPublicationBuilder
    {
        private readonly EcdsaLicenseManifestSigner _signer = new();

        public IReadOnlyList<LicenseGrantPublication> BuildClientGrants(
            LicenseManifest privateManifest,
            string keyId,
            string privateKeyPkcs8Pem)
        {
            IReadOnlyList<LicenseValidationError> errors = LicenseManifestValidator.Validate(privateManifest);
            if (errors.Count > 0)
                throw new ArgumentException("The private license directory is invalid.", nameof(privateManifest));

            var publications = new List<LicenseGrantPublication>();
            foreach (ClientLicense client in privateManifest.Clients ?? new List<ClientLicense>())
            {
                foreach (LicensedDevice device in client.Devices ?? new List<LicensedDevice>())
                {
                    if (device == null || string.IsNullOrWhiteSpace(device.DeviceId))
                        continue;

                    publications.Add(Build(
                        new LicenseGrant
                        {
                            SchemaVersion = privateManifest.SchemaVersion,
                            Revision = privateManifest.Revision,
                            IssuedAtUtc = privateManifest.IssuedAtUtc,
                            ValidUntilUtc = privateManifest.ValidUntilUtc,
                            ClientId = client.ClientId,
                            DeviceId = device.DeviceId,
                            ClientEnabled = client.Enabled,
                            DeviceEnabled = device.Enabled,
                            MinHonestFlowVersion = client.MinHonestFlowVersion,
                            OfflineGraceHours = client.OfflineGraceHours,
                            Features = client.Features?.Distinct().ToList() ?? new List<LicenseFeature>(),
                            PointAddress = device.Address
                        },
                        keyId,
                        privateKeyPkcs8Pem));
                }
            }

            return publications;
        }

        public IReadOnlyList<LicenseGrantPublication> BuildOperatorGrants(
            LicenseManifest privateManifest,
            string keyId,
            string privateKeyPkcs8Pem)
        {
            IReadOnlyList<LicenseValidationError> errors = LicenseManifestValidator.Validate(privateManifest);
            if (errors.Count > 0)
                throw new ArgumentException("The private license directory is invalid.", nameof(privateManifest));

            var publications = new List<LicenseGrantPublication>();
            foreach (ClientLicense client in privateManifest.Clients ?? new List<ClientLicense>())
            {
                foreach (OperatorDevice device in privateManifest.OperatorDevices ?? new List<OperatorDevice>())
                {
                    if (device == null || string.IsNullOrWhiteSpace(device.DeviceId))
                        continue;

                    publications.Add(Build(
                        new LicenseGrant
                        {
                            SchemaVersion = privateManifest.SchemaVersion,
                            Revision = privateManifest.Revision,
                            IssuedAtUtc = privateManifest.IssuedAtUtc,
                            ValidUntilUtc = privateManifest.ValidUntilUtc,
                            ClientId = client.ClientId,
                            DeviceId = device.DeviceId,
                            ClientEnabled = true,
                            DeviceEnabled = device.Enabled,
                            OperatorDevice = true,
                            MinHonestFlowVersion = "0.0.0",
                            OfflineGraceHours = client.OfflineGraceHours,
                            Features = new List<LicenseFeature>()
                        },
                        keyId,
                        privateKeyPkcs8Pem));
                }
            }

            return publications;
        }

        public LicenseGrantPublication Build(
            LicenseGrant grant,
            string keyId,
            string privateKeyPkcs8Pem)
        {
            if (LicenseGrantValidator.Validate(grant).Count > 0)
                throw new ArgumentException("License grant is invalid.", nameof(grant));

            byte[] grantBytes = new UTF8Encoding(false).GetBytes(
                JsonConvert.SerializeObject(grant, Formatting.Indented));
            byte[] signatureBytes = _signer.CreateSignatureFile(
                grantBytes,
                keyId,
                privateKeyPkcs8Pem);
            var request = new LicenseGrantRequest(grant.ClientId, grant.DeviceId);
            string grantRoot = "/licenses/grants/" + request.GetOpaquePathId();
            string versionPath = grantRoot + "/versions/revision-" + grant.Revision.ToString("D20");
            var pointer = new YandexLicensePublicationPointer
            {
                Revision = grant.Revision,
                VersionPath = versionPath,
                GrantSha256 = Hash(grantBytes),
                SignatureSha256 = Hash(signatureBytes),
                PublishedAtUtc = DateTimeOffset.UtcNow
            };

            return new LicenseGrantPublication(
                grantRoot,
                versionPath,
                grantBytes,
                signatureBytes,
                new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(pointer, Formatting.Indented)));
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToBase64String(SHA256.HashData(bytes));
    }

    public sealed class LicenseGrantPublication
    {
        public LicenseGrantPublication(
            string grantRoot,
            string versionPath,
            byte[] grantBytes,
            byte[] signatureBytes,
            byte[] pointerBytes)
        {
            GrantRoot = grantRoot;
            VersionPath = versionPath;
            GrantBytes = grantBytes;
            SignatureBytes = signatureBytes;
            PointerBytes = pointerBytes;
        }

        public string GrantRoot { get; }
        public string VersionPath { get; }
        public byte[] GrantBytes { get; }
        public byte[] SignatureBytes { get; }
        public byte[] PointerBytes { get; }
    }
}
