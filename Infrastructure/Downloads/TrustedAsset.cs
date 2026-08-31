using System;
using System.IO;

namespace HonestFlow.Infrastructure.Downloads
{
    /// <summary>
    /// Asset metadata received from HonestLicenseServer. The URL is only a transport
    /// location; the expected size and SHA-256 are the execution trust boundary.
    /// </summary>
    public sealed class TrustedAsset
    {
        public string FileName { get; init; }
        public string DownloadUrl { get; init; }
        public string Sha256 { get; init; }
        public long? SizeBytes { get; init; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(FileName) ||
                !string.Equals(FileName, Path.GetFileName(FileName), StringComparison.Ordinal))
                throw new InvalidDataException("Сервер вернул некорректное имя файла компонента.");
            if (!Uri.TryCreate(DownloadUrl, UriKind.Absolute, out _))
                throw new InvalidDataException("Сервер не вернул ссылку на скачивание компонента.");
            if (SizeBytes is null || SizeBytes <= 0)
                throw new InvalidDataException("Сервер не вернул размер компонента.");
            if (string.IsNullOrWhiteSpace(Sha256) || Sha256.Length != 64 || !IsSha256(Sha256))
                throw new InvalidDataException("Сервер не вернул корректную SHA-256 сумму компонента.");
        }

        private static bool IsSha256(string value)
        {
            try { return Convert.FromHexString(value).Length == 32; }
            catch (FormatException) { return false; }
        }
    }
}
