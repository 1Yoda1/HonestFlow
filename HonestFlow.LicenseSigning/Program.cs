using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HonestFlow.Infrastructure.Updates;
using Newtonsoft.Json;

namespace HonestFlow.LicenseSigning
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine(
                    "Usage: HonestFlow.LicenseSigning <HonestFlow.exe> <key-id> <private-key.pem> <output-folder>");
                return 2;
            }

            try
            {
                string sourceExe = Path.GetFullPath(args[0]);
                string keyId = args[1]?.Trim();
                string privateKeyPath = Path.GetFullPath(args[2]);
                string outputRoot = Path.GetFullPath(args[3]);

                if (!File.Exists(sourceExe))
                    throw new FileNotFoundException("HonestFlow executable was not found.", sourceExe);
                if (!File.Exists(privateKeyPath))
                    throw new FileNotFoundException("Private key file was not found.", privateKeyPath);
                if (string.IsNullOrWhiteSpace(keyId))
                    throw new ArgumentException("Key id is required.");

                string versionText = FileVersionInfo.GetVersionInfo(sourceExe).FileVersion;
                if (!Version.TryParse(versionText, out Version version))
                    throw new InvalidDataException("Executable file version is invalid.");

                string versionFolder = Path.Combine(outputRoot, version.ToString());
                Directory.CreateDirectory(versionFolder);
                string assetPath = Path.Combine(versionFolder, SelfUpdateManifest.ExpectedAssetName);
                File.Copy(sourceExe, assetPath, overwrite: true);

                byte[] assetBytes = File.ReadAllBytes(assetPath);
                var manifest = new SelfUpdateManifest
                {
                    SchemaVersion = SelfUpdateManifest.CurrentSchemaVersion,
                    Version = version.ToString(),
                    AssetName = SelfUpdateManifest.ExpectedAssetName,
                    Size = assetBytes.LongLength,
                    Sha256 = Convert.ToHexString(SHA256.HashData(assetBytes))
                };

                byte[] manifestBytes = new UTF8Encoding(false).GetBytes(
                    JsonConvert.SerializeObject(manifest, Formatting.Indented));
                byte[] signatureBytes = new EcdsaLicenseManifestSigner().CreateSignatureFile(
                    manifestBytes,
                    keyId,
                    File.ReadAllText(privateKeyPath));

                File.WriteAllBytes(Path.Combine(versionFolder, "update.json"), manifestBytes);
                File.WriteAllBytes(Path.Combine(versionFolder, "update.json.sig"), signatureBytes);

                Console.WriteLine(versionFolder);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
