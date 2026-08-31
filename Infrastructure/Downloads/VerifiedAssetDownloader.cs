using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Downloads
{
    public delegate Task<HttpResponseMessage> TrustedAssetRequestSender(
        HttpRequestMessage request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads an asset to a local cache only after its server-provided size and
    /// SHA-256 have both been verified. Existing cache entries are verified too.
    /// </summary>
    public sealed class VerifiedAssetDownloader
    {
        public async Task<string> GetVerifiedAsync(
            TrustedAsset asset,
            string destinationFolder,
            TrustedAssetRequestSender send,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            if (asset is null) throw new ArgumentNullException(nameof(asset));
            if (send is null) throw new ArgumentNullException(nameof(send));
            asset.Validate();
            if (string.IsNullOrWhiteSpace(destinationFolder))
                throw new ArgumentException("Папка кэша обязательна.", nameof(destinationFolder));

            Directory.CreateDirectory(destinationFolder);
            string destination = Path.Combine(destinationFolder, asset.FileName);
            if (IsVerified(destination, asset))
            {
                progress?.Report(100);
                return destination;
            }

            DeleteIfExists(destination);
            string partial = destination + ".partial";
            DeleteIfExists(partial);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
                using HttpResponseMessage response = await send(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, useAsync: true))
                {
                    byte[] buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written += read;
                        progress?.Report((int)Math.Min(99, written * 100 / asset.SizeBytes!.Value));
                    }
                    await output.FlushAsync(cancellationToken);
                }

                if (!IsVerified(partial, asset))
                    throw new InvalidDataException("Скачанный файл не прошёл проверку размера или SHA-256.");

                File.Move(partial, destination, overwrite: true);
                progress?.Report(100);
                return destination;
            }
            finally
            {
                DeleteIfExists(partial);
            }
        }

        public bool IsVerified(string path, TrustedAsset asset)
        {
            if (asset is null) throw new ArgumentNullException(nameof(asset));
            asset.Validate();
            if (!File.Exists(path) || new FileInfo(path).Length != asset.SizeBytes!.Value)
                return false;

            using FileStream stream = File.OpenRead(path);
            byte[] actual = SHA256.HashData(stream);
            byte[] expected = Convert.FromHexString(asset.Sha256);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
