using System;
using System.Security.Cryptography;
using HonestFlow.Application.Security;
using HonestFlow.Models;
using Xunit;

namespace HonestFlow.Tests
{
    public sealed class EngineerAccessServiceTests
    {
        [Fact]
        public void CheckAccess_WhenNotConfigured_AllowsLegacyClient()
        {
            var service = new EngineerAccessService();

            EngineerAccessResult result = service.CheckAccess(new IPData { ClientId = "client-1" });

            Assert.True(result.IsAllowed);
            Assert.Equal("ENGINEER_PASSWORD_NOT_CONFIGURED_LEGACY_ALLOWED", result.TechnicalCode);
        }

        [Fact]
        public void Unlock_WithCorrectPassword_UnlocksSession()
        {
            DateTimeOffset now = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
            var service = new EngineerAccessService(utcNow: () => now);
            IPData client = Client("correct-password");

            EngineerAccessResult result = service.Unlock(client, "correct-password");

            Assert.True(result.IsAllowed);
            Assert.Equal("ENGINEER_UNLOCKED", result.TechnicalCode);
            Assert.True(service.CheckAccess(client).IsAllowed);
        }

        [Fact]
        public void Unlock_WithWrongPassword_DoesNotUnlock()
        {
            var service = new EngineerAccessService();
            IPData client = Client("correct-password");

            EngineerAccessResult result = service.Unlock(client, "wrong-password");

            Assert.False(result.IsAllowed);
            Assert.True(result.PasswordRequired);
            Assert.Equal("ENGINEER_PASSWORD_INVALID", result.TechnicalCode);
        }

        [Fact]
        public void CheckAccess_AfterTimePasses_RemainsUnlockedUntilExplicitLock()
        {
            DateTimeOffset now = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
            var service = new EngineerAccessService(utcNow: () => now);
            IPData client = Client("correct-password");
            service.Unlock(client, "correct-password");

            now = now.AddDays(1);
            Assert.True(service.CheckAccess(client).IsAllowed);

            service.Lock();
            EngineerAccessResult result = service.CheckAccess(client);

            Assert.False(result.IsAllowed);
            Assert.True(result.PasswordRequired);
        }

        [Fact]
        public void Unlock_AfterMaximumFailures_TemporarilyLocksAccess()
        {
            DateTimeOffset now = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
            var service = new EngineerAccessService(
                utcNow: () => now,
                maximumAttempts: 3);
            IPData client = Client("correct-password");

            service.Unlock(client, "wrong-1");
            service.Unlock(client, "wrong-2");
            EngineerAccessResult result = service.Unlock(client, "wrong-3");

            Assert.False(result.IsAllowed);
            Assert.False(result.PasswordRequired);
            Assert.Equal("ENGINEER_TEMPORARILY_LOCKED", result.TechnicalCode);
        }

        [Fact]
        public void CheckAccess_WithDamagedConfiguration_DeniesSafely()
        {
            var service = new EngineerAccessService();
            var client = new IPData
            {
                ClientId = "client-1",
                EngineerAccess = new EngineerAccessSettings
                {
                    Algorithm = EngineerPasswordHasher.SupportedAlgorithm,
                    Iterations = EngineerPasswordHasher.MinimumIterations,
                    SaltBase64 = "broken",
                    PasswordHashBase64 = "broken"
                }
            };

            EngineerAccessResult result = service.CheckAccess(client);

            Assert.False(result.IsAllowed);
            Assert.Equal("ENGINEER_CONFIGURATION_INVALID", result.TechnicalCode);
        }

        private static IPData Client(string password)
        {
            byte[] salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                EngineerPasswordHasher.MinimumIterations,
                HashAlgorithmName.SHA256,
                32);
            return new IPData
            {
                ClientId = "client-1",
                EngineerAccess = new EngineerAccessSettings
                {
                    Algorithm = EngineerPasswordHasher.SupportedAlgorithm,
                    Iterations = EngineerPasswordHasher.MinimumIterations,
                    SaltBase64 = Convert.ToBase64String(salt),
                    PasswordHashBase64 = Convert.ToBase64String(hash)
                }
            };
        }
    }
}
