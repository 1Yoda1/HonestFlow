using System;
using System.Security.Cryptography;
using System.Text;

namespace HonestFlow.Infrastructure
{
    public static class PasswordHelper
    {
        /// <summary>
        /// Создать хеш пароля (SHA256)
        /// </summary>
        public static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Проверить пароль
        /// </summary>
        public static bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }

        /// <summary>
        /// Простая обфускация для хранения в JSON (не криптостойкая, но лучше plain text)
        /// </summary>
        public static string ObfuscatePassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return password;
            var bytes = Encoding.UTF8.GetBytes(password);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ 0xAB);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Деобфускация
        /// </summary>
        public static string DeobfuscatePassword(string obfuscated)
        {
            if (string.IsNullOrEmpty(obfuscated)) return obfuscated;
            var bytes = Convert.FromBase64String(obfuscated);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ 0xAB);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}