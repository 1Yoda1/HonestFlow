using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.DeviceIdentity;
using HonestFlow.Models;
using HonestFlow.Infrastructure;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseObservationService : ILicenseObservationService
    {
        private const string ModuleName = nameof(LicenseObservationService);
        private readonly ILicenseManifestRepository _remoteRepository;
        private readonly ILicenseManifestCache _cache;
        private readonly IDeviceIdentityService _deviceIdentityService;
        private readonly ILicenseDecisionService _decisionService;
        private readonly ILicenseObservationSnapshotStore _snapshotStore;
        private readonly LicenseEnforcementMode _mode;
        private readonly Func<DateTimeOffset> _utcNowProvider;
        private readonly Func<Version> _versionProvider;
        private readonly ITrustedLicenseClock _trustedClock;
        private readonly Func<bool?> _onlineClientPolicyEnabledProvider;

        public LicenseObservationService(
            ILicenseManifestRepository remoteRepository,
            ILicenseManifestCache cache,
            IDeviceIdentityService deviceIdentityService,
            ILicenseDecisionService decisionService,
            ILicenseObservationSnapshotStore snapshotStore,
            LicenseEnforcementMode mode,
            Func<DateTimeOffset> utcNowProvider = null,
            Func<Version> versionProvider = null,
            ITrustedLicenseClock trustedClock = null,
            Func<bool?> onlineClientPolicyEnabledProvider = null)
        {
            _remoteRepository = remoteRepository ?? throw new ArgumentNullException(nameof(remoteRepository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _deviceIdentityService = deviceIdentityService ?? throw new ArgumentNullException(nameof(deviceIdentityService));
            _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
            _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
            _mode = mode;
            _trustedClock = trustedClock;
            _utcNowProvider = utcNowProvider ?? (() => _trustedClock?.UtcNow ?? DateTimeOffset.UtcNow);
            _versionProvider = versionProvider ?? (() =>
                Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0));
            _onlineClientPolicyEnabledProvider = onlineClientPolicyEnabledProvider;
        }

        public async Task<LicenseObservationSnapshot> ObserveAsync(
            IPData client,
            CancellationToken cancellationToken)
        {
            DateTimeOffset observedAtUtc = _utcNowProvider().ToUniversalTime();
            if (_mode == LicenseEnforcementMode.Disabled)
            {
                var disabled = BuildInvalidSnapshot(
                    observedAtUtc,
                    LicenseManifestReadStatus.ServerError,
                    null,
                    "LICENSE_OBSERVATION_DISABLED",
                    "Наблюдение лицензии отключено конфигурацией.");
                Publish(disabled);
                return disabled;
            }

            try
            {
                DeviceIdentityResult identity = await _deviceIdentityService.GetOrCreateAsync(cancellationToken);
                if (!identity.IsAvailable)
                {
                    var unavailableIdentity = BuildInvalidSnapshot(
                        observedAtUtc,
                        LicenseManifestReadStatus.ServerError,
                        null,
                        "LICENSE_DEVICE_ID_UNAVAILABLE",
                        "DeviceId установки недоступен.");
                    Publish(unavailableIdentity);
                    return unavailableIdentity;
                }

                var grantRequest = new LicenseGrantRequest(client?.ClientId, identity.DeviceId);
                LicenseManifestReadResult remote = await _remoteRepository.ReadAsync(
                    grantRequest,
                    cancellationToken);
                if (remote.IsSuccess)
                {
                    _trustedClock?.ObserveTrustedTime(
                        remote.Grant.IssuedAtUtc,
                        remote.Grant.ValidUntilUtc,
                        remote.ServerDateUtc);
                    observedAtUtc = _utcNowProvider().ToUniversalTime();

                    LicenseCacheWriteResult cacheWrite = remote.Cacheable
                        ? await _cache.SaveAsync(
                            grantRequest,
                            remote,
                            observedAtUtc,
                            cancellationToken)
                        : LicenseCacheWriteResult.Failure(
                            LicenseCacheStatus.WriteFailed,
                            "LegacyGrantUsesExistingCache");
                    if (!cacheWrite.IsSuccess)
                    {
                        Logger.Warning(
                            $"Event=LicenseObservationCacheWrite Status={cacheWrite.Status} " +
                            $"ErrorCode={cacheWrite.ErrorCode}",
                            ModuleName);

                        if (cacheWrite.Status == LicenseCacheStatus.StaleRevision)
                        {
                            LicenseCacheReadResult newerCache = await _cache.ReadAsync(
                                grantRequest,
                                cancellationToken);
                            if (newerCache.IsSuccess)
                            {
                                LicenseObservationSnapshot rollbackProtectedSnapshot = Decide(
                                    client,
                                    identity.DeviceId,
                                    newerCache.Grant,
                                    LicenseManifestSource.Cache,
                                    newerCache.LastSuccessfulOnlineCheckUtc,
                                    remote.Status,
                                    newerCache.Status,
                                    observedAtUtc);
                                Publish(rollbackProtectedSnapshot);
                                return rollbackProtectedSnapshot;
                            }

                            var rollbackRejected = BuildInvalidSnapshot(
                                observedAtUtc,
                                remote.Status,
                                newerCache.Status,
                                "LICENSE_REMOTE_REVISION_ROLLBACK_REJECTED",
                                "Удалённая лицензия имеет более старую ревизию, чем проверенный локальный кеш.");
                            Publish(rollbackRejected);
                            return rollbackRejected;
                        }
                    }

                    LicenseObservationSnapshot remoteSnapshot = Decide(
                        client,
                        identity.DeviceId,
                        remote.Grant,
                        LicenseManifestSource.Remote,
                        observedAtUtc,
                        remote.Status,
                        cacheWrite.Status,
                        observedAtUtc);
                    Publish(remoteSnapshot);
                    return remoteSnapshot;
                }

                if (remote.Status == LicenseManifestReadStatus.NotFound)
                {
                    var notIssued = new LicenseObservationSnapshot
                    {
                        ObservedAtUtc = observedAtUtc,
                        ClientId = client?.ClientId,
                        DeviceId = identity.DeviceId,
                        EnforcementMode = _mode,
                        RemoteStatus = remote.Status,
                        Decision = LicenseDecision.LicenseNotIssued,
                        TechnicalCode = "LICENSE_GRANT_NOT_FOUND",
                        Message = "Устройство зарегистрировано. Лицензия для этого компьютера ещё не выдана."
                    };
                    Publish(notIssued);
                    return notIssued;
                }

                if (CanUseCache(remote.Status))
                {
                    LicenseCacheReadResult cached = await _cache.ReadAsync(
                        grantRequest,
                        cancellationToken);
                    if (cached.IsSuccess)
                    {
                        LicenseObservationSnapshot cacheSnapshot = Decide(
                            client,
                            identity.DeviceId,
                            cached.Grant,
                            LicenseManifestSource.Cache,
                            cached.LastSuccessfulOnlineCheckUtc,
                            remote.Status,
                            cached.Status,
                            observedAtUtc);
                        Publish(cacheSnapshot);
                        return cacheSnapshot;
                    }

                    var noCache = BuildInvalidSnapshot(
                        observedAtUtc,
                        remote.Status,
                        cached.Status,
                        "LICENSE_REMOTE_UNAVAILABLE_AND_CACHE_UNAVAILABLE",
                        "Удалённая лицензия недоступна, проверенный кеш отсутствует.");
                    Publish(noCache);
                    return noCache;
                }

                var invalidRemote = BuildInvalidSnapshot(
                    observedAtUtc,
                    remote.Status,
                    null,
                    "LICENSE_REMOTE_INVALID_" + remote.Status.ToString().ToUpperInvariant(),
                    "Удалённая лицензия не прошла проверку.");
                Publish(invalidRemote);
                return invalidRemote;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"Event=LicenseObservationFailed ErrorType={ex.GetType().Name}",
                    ModuleName);
                var failed = BuildInvalidSnapshot(
                    observedAtUtc,
                    LicenseManifestReadStatus.ServerError,
                    null,
                    "LICENSE_OBSERVATION_FAILED",
                    "Не удалось выполнить наблюдение лицензии.");
                Publish(failed);
                return failed;
            }
        }

        private LicenseObservationSnapshot Decide(
            IPData client,
            string deviceId,
            Models.Licensing.LicenseGrant grant,
            LicenseManifestSource source,
            DateTimeOffset? lastOnlineCheckUtc,
            LicenseManifestReadStatus remoteStatus,
            LicenseCacheStatus? cacheStatus,
            DateTimeOffset observedAtUtc)
        {
            LicenseDecisionResult decision = _decisionService.Decide(new LicenseDecisionContext
            {
                ClientId = client?.ClientId,
                DeviceId = deviceId,
                CurrentHonestFlowVersion = _versionProvider(),
                Grant = grant,
                ManifestSource = source,
                LastSuccessfulOnlineCheckUtc = lastOnlineCheckUtc,
                OnlineClientPolicyEnabled = source == LicenseManifestSource.Remote
                    ? _onlineClientPolicyEnabledProvider?.Invoke()
                    : null
            });

            return new LicenseObservationSnapshot
            {
                ObservedAtUtc = observedAtUtc,
                ClientId = client?.ClientId,
                DeviceId = deviceId,
                PointAddress = decision.PointAddress,
                LastSuccessfulOnlineCheckUtc = lastOnlineCheckUtc?.ToUniversalTime(),
                EnforcementMode = _mode,
                ManifestSource = source,
                RemoteStatus = remoteStatus,
                CacheStatus = cacheStatus,
                Revision = grant?.Revision,
                Decision = decision.Decision,
                TechnicalCode = decision.TechnicalCode,
                Message = decision.Message,
                OfflineGraceEndsAtUtc = decision.OfflineGraceEndsAtUtc,
                MinimumRequiredVersion = decision.MinimumRequiredVersion,
                Features = decision.Features.ToArray()
            };
        }

        private LicenseObservationSnapshot BuildInvalidSnapshot(
            DateTimeOffset observedAtUtc,
            LicenseManifestReadStatus remoteStatus,
            LicenseCacheStatus? cacheStatus,
            string technicalCode,
            string message)
        {
            return new LicenseObservationSnapshot
            {
                ObservedAtUtc = observedAtUtc,
                EnforcementMode = _mode,
                RemoteStatus = remoteStatus,
                CacheStatus = cacheStatus,
                Decision = LicenseDecision.InvalidLicenseState,
                TechnicalCode = technicalCode,
                Message = message
            };
        }

        private void Publish(LicenseObservationSnapshot snapshot)
        {
            _snapshotStore.Set(snapshot);
            Logger.Info(
                $"Event=LicenseDecision Mode={snapshot.EnforcementMode} " +
                $"Source={(snapshot.ManifestSource?.ToString() ?? "None")} " +
                $"RemoteStatus={snapshot.RemoteStatus} " +
                $"CacheStatus={(snapshot.CacheStatus?.ToString() ?? "None")} " +
                $"Decision={snapshot.Decision} TechnicalCode={snapshot.TechnicalCode} " +
                $"ObservedAtUtc={snapshot.ObservedAtUtc:O}",
                ModuleName);
        }

        private static bool CanUseCache(LicenseManifestReadStatus status)
        {
            return status == LicenseManifestReadStatus.NetworkUnavailable ||
                   status == LicenseManifestReadStatus.Timeout ||
                   status == LicenseManifestReadStatus.ServerError;
        }
    }
}
