using System;
using System.Security.Cryptography;
using HonestFlow.Models;

namespace HonestFlow.Application.Security
{
    public sealed class EngineerPasswordHasher
    {
        public const string SupportedAlgorithm = "PBKDF2-SHA256";
        public const int MinimumIterations = 100000;
        private const int HashSize = 32;

        public bool IsConfigurationValid(EngineerAccessSettings settings)
        {
            if (settings == null ||
                !string.Equals(settings.Algorithm, SupportedAlgorithm, StringComparison.Ordinal) ||
                settings.Iterations < MinimumIterations ||
                string.IsNullOrWhiteSpace(settings.SaltBase64) ||
                string.IsNullOrWhiteSpace(settings.PasswordHashBase64))
            {
                return false;
            }

            try
            {
                return Convert.FromBase64String(settings.SaltBase64).Length >= 16 &&
                    Convert.FromBase64String(settings.PasswordHashBase64).Length == HashSize;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool Verify(EngineerAccessSettings settings, string password)
        {
            if (!IsConfigurationValid(settings) || string.IsNullOrEmpty(password))
                return false;

            byte[] salt = Convert.FromBase64String(settings.SaltBase64);
            byte[] expected = Convert.FromBase64String(settings.PasswordHashBase64);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                settings.Iterations,
                HashAlgorithmName.SHA256,
                HashSize);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
