using System;
using System.Text;

namespace HonestFlow.Infrastructure
{
    /// <summary>
    /// Совместимая маскировка ips.json: XOR 0xAA + Base64.
    /// Это не криптографическая защита, а формат хранения текущей версии приложения.
    /// </summary>
    public static class ObfuscationService
    {
        public static string Obfuscate(string plainText)
        {
            var bytes = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ 0xAA);
            return Convert.ToBase64String(bytes);
        }

        public static string Deobfuscate(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
                return string.Empty;

            var bytes = Convert.FromBase64String(cipherText);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ 0xAA);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
