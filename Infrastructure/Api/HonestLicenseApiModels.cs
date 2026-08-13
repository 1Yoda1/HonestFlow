using System;
using System.Collections.Generic;

namespace HonestFlow.Infrastructure.Api
{
    public sealed class ApiTokenResponse
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresInSeconds { get; set; }
        public bool DeviceRegistrationRequired { get; set; }
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public bool? LicensePolicyEnabled { get; set; }
    }

    public sealed class ApiLicenseResponse
    {
        public string GrantBase64 { get; set; }
        public string SignatureBase64 { get; set; }
        public string KeyId { get; set; }
        public long Revision { get; set; }
        public DateTimeOffset IssuedAtUtc { get; set; }
        public DateTimeOffset ValidUntilUtc { get; set; }
    }

    public sealed class ApiSession
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTimeOffset AccessTokenExpiresAtUtc { get; set; }
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string ExternalDeviceId { get; set; }
        public bool RememberActiveSession { get; set; }
        public bool? LicensePolicyEnabled { get; set; }
    }

    public sealed class ApiConfigurationResponse
    {
        public DateTimeOffset ConfigurationRevision { get; set; }
        public ApiClientConfiguration Client { get; set; }
        public ApiDeviceConfiguration Device { get; set; }
        public List<ApiComponentConfiguration> Components { get; set; } = new();
    }

    public sealed class ApiClientConfiguration
    {
        public string ClientId { get; set; }
        public string Name { get; set; }
        public string Architecture { get; set; }
        public bool HasLmDatabaseBackup { get; set; }
        public bool RuDesktopEnabled { get; set; }
        public bool RuDesktopAutoOfferPasswordSetup { get; set; }
    }

    public sealed class ApiDeviceConfiguration
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Status { get; set; }
    }

    public sealed class ApiComponentConfiguration
    {
        public string Component { get; set; }
        public string EffectiveVersion { get; set; }
        public string DownloadUrl { get; set; }
    }

    public sealed class ApiRegistrationStatusResponse
    {
        public string DeviceId { get; set; }
        public string Status { get; set; }
        public DateTimeOffset RequestedAtUtc { get; set; }
        public DateTimeOffset? ResolvedAtUtc { get; set; }
        public string Comment { get; set; }
    }
}
