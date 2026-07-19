using HonestFlow.Application.Core;
using HonestFlow.Application.Diagnostics;
using HonestFlow.Application.Licensing;
using HonestFlow.Infrastructure.Configuration;
using Newtonsoft.Json;
using System;
using System.IO;

namespace HonestFlow.Application.PointIdentity
{
    public sealed class PointAddressService : IPointAddressService
    {
        public const int MaximumAddressLength = 500;

        private readonly ILogService _log;
        private readonly string _stateFile;
        private readonly Func<string> _kktAddressProvider;
        private readonly object _sync = new();

        public PointAddressService(
            ILogService log,
            string stateFile = null,
            Func<string> kktAddressProvider = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _stateFile = string.IsNullOrWhiteSpace(stateFile)
                ? AppPaths.PointAddressFile
                : stateFile;
            _kktAddressProvider = kktAddressProvider ?? KktFiscalAddressService.TryFindAddress;
        }

        public PointAddressResult Resolve(LicenseObservationSnapshot snapshot)
        {
            string deviceId = NormalizeDeviceId(snapshot?.DeviceId);
            string licenseAddress = NormalizeAddress(snapshot?.PointAddress);
            if (licenseAddress != null)
            {
                Save(deviceId, licenseAddress, PointAddressSource.License);
                return Result(licenseAddress, PointAddressSource.License);
            }

            PointAddressState state = Load();
            if (state != null &&
                deviceId != null &&
                string.Equals(state.DeviceId, deviceId, StringComparison.Ordinal) &&
                NormalizeAddress(state.Address) is string localAddress)
            {
                return Result(localAddress, PointAddressSource.LocalCache);
            }

            string kktAddress = NormalizeAddress(_kktAddressProvider());
            if (kktAddress != null)
            {
                Save(deviceId, kktAddress, PointAddressSource.KktLog);
                return Result(kktAddress, PointAddressSource.KktLog);
            }

            return Result(null, PointAddressSource.None);
        }

        public void Save(string deviceId, string address, PointAddressSource source)
        {
            deviceId = NormalizeDeviceId(deviceId);
            address = NormalizeAddress(address);
            if (deviceId == null || address == null)
                return;

            var state = new PointAddressState
            {
                DeviceId = deviceId,
                Address = address,
                Source = source,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            lock (_sync)
            {
                try
                {
                    string fullPath = Path.GetFullPath(_stateFile);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.WriteAllText(
                            temporaryPath,
                            JsonConvert.SerializeObject(state, Formatting.Indented));
                        File.Move(temporaryPath, fullPath, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug($"Point address save failed: {ex.GetType().Name}");
                }
            }
        }

        public static string NormalizeAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            string normalized = address.Trim();
            return normalized.Length <= MaximumAddressLength ? normalized : null;
        }

        private PointAddressState Load()
        {
            lock (_sync)
            {
                try
                {
                    if (!File.Exists(_stateFile))
                        return null;

                    var state = JsonConvert.DeserializeObject<PointAddressState>(
                        File.ReadAllText(_stateFile));
                    return state?.SchemaVersion == 1 ? state : null;
                }
                catch (Exception ex)
                {
                    _log.LogDebug($"Point address cache ignored: {ex.GetType().Name}");
                    return null;
                }
            }
        }

        private static string NormalizeDeviceId(string deviceId)
        {
            return string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        }

        private static PointAddressResult Result(string address, PointAddressSource source)
        {
            return new PointAddressResult { Address = address, Source = source };
        }
    }
}
