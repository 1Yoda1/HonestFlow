using System;
using HonestFlow.Models;

namespace HonestFlow.Application.Security
{
    public sealed class EngineerAccessService : IEngineerAccessService
    {
        private readonly EngineerPasswordHasher _hasher;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly TimeSpan _lockoutDuration;
        private readonly int _maximumAttempts;
        private string _unlockedClientId;
        private string _failedClientId;
        private int _failedAttempts;
        private DateTimeOffset? _retryAfterUtc;

        public EngineerAccessService(
            EngineerPasswordHasher hasher = null,
            Func<DateTimeOffset> utcNow = null,
            TimeSpan? lockoutDuration = null,
            int maximumAttempts = 5)
        {
            if (maximumAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumAttempts));

            _hasher = hasher ?? new EngineerPasswordHasher();
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _lockoutDuration = lockoutDuration ?? TimeSpan.FromMinutes(1);
            _maximumAttempts = maximumAttempts;
        }

        public EngineerAccessResult CheckAccess(IPData client)
        {
            if (client == null)
                return Denied("ENGINEER_CLIENT_MISSING", "Клиент не определён.");

            if (client.EngineerAccess == null)
            {
                return new EngineerAccessResult(
                    true,
                    false,
                    "ENGINEER_PASSWORD_NOT_CONFIGURED_LEGACY_ALLOWED",
                    "Инженерный пароль для клиента пока не настроен.");
            }

            if (!_hasher.IsConfigurationValid(client.EngineerAccess) ||
                string.IsNullOrWhiteSpace(client.ClientId))
            {
                return Denied(
                    "ENGINEER_CONFIGURATION_INVALID",
                    "Настройки инженерного доступа повреждены. Обратитесь к администратору.");
            }

            DateTimeOffset now = _utcNow();
            if (string.Equals(_unlockedClientId, client.ClientId, StringComparison.Ordinal))
            {
                return new EngineerAccessResult(
                    true,
                    false,
                    "ENGINEER_SESSION_ACTIVE",
                    "Режим инженера активен до закрытия HonestFlow.");
            }

            if (IsLockedOut(client.ClientId, now))
            {
                return new EngineerAccessResult(
                    false,
                    false,
                    "ENGINEER_TEMPORARILY_LOCKED",
                    "Слишком много неверных попыток. Повторите позже.",
                    retryAfterUtc: _retryAfterUtc);
            }

            return new EngineerAccessResult(
                false,
                true,
                "ENGINEER_PASSWORD_REQUIRED",
                "Для этой операции требуется пароль инженера.");
        }

        public EngineerAccessResult Unlock(IPData client, string password)
        {
            EngineerAccessResult current = CheckAccess(client);
            if (current.IsAllowed || !current.PasswordRequired)
                return current;

            if (_hasher.Verify(client.EngineerAccess, password))
            {
                _unlockedClientId = client.ClientId;
                ResetFailures();
                return new EngineerAccessResult(
                    true,
                    false,
                    "ENGINEER_UNLOCKED",
                    "Режим инженера активирован до закрытия HonestFlow.");
            }

            RegisterFailure(client.ClientId);
            if (_retryAfterUtc.HasValue)
            {
                return new EngineerAccessResult(
                    false,
                    false,
                    "ENGINEER_TEMPORARILY_LOCKED",
                    "Слишком много неверных попыток. Повторите через минуту.",
                    retryAfterUtc: _retryAfterUtc);
            }

            return new EngineerAccessResult(
                false,
                true,
                "ENGINEER_PASSWORD_INVALID",
                "Неверный пароль инженера.");
        }

        public void Lock()
        {
            _unlockedClientId = null;
        }

        private bool IsLockedOut(string clientId, DateTimeOffset now)
        {
            if (!string.Equals(_failedClientId, clientId, StringComparison.Ordinal) ||
                !_retryAfterUtc.HasValue)
            {
                return false;
            }

            if (_retryAfterUtc > now)
                return true;

            ResetFailures();
            return false;
        }

        private void RegisterFailure(string clientId)
        {
            if (!string.Equals(_failedClientId, clientId, StringComparison.Ordinal))
            {
                _failedClientId = clientId;
                _failedAttempts = 0;
                _retryAfterUtc = null;
            }

            _failedAttempts++;
            if (_failedAttempts >= _maximumAttempts)
                _retryAfterUtc = _utcNow().Add(_lockoutDuration);
        }

        private void ResetFailures()
        {
            _failedClientId = null;
            _failedAttempts = 0;
            _retryAfterUtc = null;
        }

        private static EngineerAccessResult Denied(string code, string message) =>
            new(false, false, code, message);
    }
}
