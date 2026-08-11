# HonestFlow system map

Read this file before searching or changing code. The system consists of three sibling repositories:

- `C:\Work\HonestFlow` — Windows client application used at a workplace.
- `C:\Work\HonestDesk` — Windows WPF administration and support application.
- `C:\Work\HonestLicenseServer` — ASP.NET Core API backed by SQLite.

Normal data flow: `HonestFlow` and `HonestDesk` call `HonestLicenseServer`; the server is the source of truth for clients, devices, registrations, configuration and published licenses. `HonestDesk` signs grants locally and publishes them through the admin API. The server stores and verifies them; `HonestFlow` downloads and verifies them locally.

## HonestFlow

Purpose: production workplace client, startup/authentication, configuration, license enforcement, updates and the main operator UI.

### UI architecture (preserve these rules)

HonestFlow 3.0 is a WPF application. Its production startup path is:

`HonestFlow.WpfPrototype/App.xaml` → `StartupWindow.xaml` → `MainWindow`.

`HonestFlow.WpfPrototype` is a historical project name; it is the current production WPF project and must not be renamed without an explicit task.

The WinForms project and root `Program.cs` startup remain legacy/fallback code. Do not add new HonestFlow 3.0 user-facing features to WinForms unless the user explicitly requests a legacy implementation.

New UI features belong in the WPF startup and main-window flow. Shared application and infrastructure code must remain UI-independent and must not be duplicated separately for WPF and WinForms.

### Important paths

- Entry point/UI: `HonestFlow.WpfPrototype/App.xaml`, `App.xaml.cs`, `StartupWindow.xaml*`, `MainWindow.xaml*`.
- Use cases/workflows: `Application/`; startup orchestration is in `Application/Bootstrap/`, auth in `Application/Auth/`, licensing and registration in `Application/Licensing/`.
- API clients/session: `Infrastructure/Api/` (`ApiAuthService`, `ApiSessionService`, registration status provider).
- License/configuration infrastructure: `Infrastructure/Licensing/`, `Infrastructure/Configuration/`.
- Shared models: root `Models/` plus feature models beside their workflows under `Application/`.
- Composition: `Infrastructure/Composition/`.
- Tests: `HonestFlow.Tests/`.
- Legacy UI: `Forms/`, root `Program.cs`.

### Production startup flow

`password` → `POST /api/auth/login` → opaque access/refresh tokens + `clientId/clientName` + `deviceRegistrationRequired`.

- Registered Active device: fetch `GET /api/configuration/current` and `GET /api/license/current`, run the existing license pipeline, then open `MainWindow` only when the decision is `Allowed`.
- Unknown or Deleted device: restricted `StartupWindow` → `GET /api/device/registration/current` → address entry → `POST /api/device/request` → manual status checks.
- Approved registration: refresh the existing session → fetch configuration/license through the existing pipeline → open `MainWindow` when allowed. Do not require application restart.
- `ApiSessionService` owns token persistence/rotation; do not duplicate login, refresh or bearer logic in UI code.

## HonestDesk

Purpose: trusted WPF admin/support client for server-backed client, device, registration, configuration and license operations.

### Important paths

- Entry point: `C:\Work\HonestDesk\HonestDesk\App.xaml` and `App.xaml.cs`; main shell is `MainWindow.xaml*`.
- Admin UI: `AdminAccessWindow.xaml*`, `AdminEditorWindow.xaml*`, `ClientProfileWindow.xaml*`.
- Device/registration UI: `OperatorDevicesWindow.xaml*`, `DeviceLicensePolicyWindow.xaml*`.
- License UI: `LicenseManagerWindow.xaml*`.
- API client/contracts: `HonestDesk/Infrastructure/Api/HonestLicenseAdminApiClient.cs`.
- Signing/publication and server license services: `HonestDesk/Infrastructure/Licensing/`.
- Local settings/state/logging: `HonestDesk/Services/`.
- Models: `HonestDesk/Models/`; shared safe configuration types: `HonestFlow.Config.Core/`.
- Tests: `HonestDesk.Tests/`.

### Server-backed responsibilities

- Clients: load, create, edit, enable/disable.
- Devices: load, edit name/address/comment, Active/Disabled/Deleted transitions; Deleted is hidden from normal lists.
- Registration requests: list Pending/history, Approve or Reject; display requested address and HonestFlow version.
- Licenses: create signed personal grants, publish, inspect history and revoke.
- Integration settings: identification code, CHZ token and RuDesktop flags.
- Support requests: load and resolve.

HonestLicenseServer is the source of truth. A UI action is complete only after a successful API response; reload must reproduce the same state. Published grants are not the source of truth for Client/Device records. The private ECDSA signing key stays in HonestDesk and must never be copied to the server, HonestFlow, logs or Git.

## HonestLicenseServer

Purpose: ASP.NET Core/.NET API for authentication, sessions, device registration, configuration, versions/assets, licenses and administration.

### Important paths

- Entry/composition: `C:\Work\HonestLicenseServer\Program.cs`.
- HTTP endpoints: `Controllers/`.
- Public request/response contracts: `Contracts/`; canonical OpenAPI: `openapi/honest-license-v1.json`.
- Authentication/authorization handlers and claims: `Authentication/`.
- EF Core context, schema updater, hashing/token helpers: `Data/`.
- Persisted entities: `Models/DatabaseModels.cs`.
- Supporting services: `Infrastructure/` (rate limiting helpers, signature verification, API errors, asset resolver, notifications). There is no separate server `Services/` directory.
- Generated consumer: `clients/HonestLicense.Client/`.
- API/architecture docs: `docs/API-RU.md`, `docs/architecture-decisions.md`.
- Integration tests: `tests/HonestLicenseServer.IntegrationTests/`.
- Deployment: `deploy-production.ps1`, `deploy/`; load tests: `load-tests/`.

### Main controllers and flows

- `AuthController`: `login`, rotating `refresh`, `logout`. Login validates the active credential/client and distinguishes Active, Disabled, Deleted and unknown devices.
- Tokens are opaque and only hashes are stored. Access lifetime is 15 minutes; refresh lifetime is 30 days. Refresh revokes/replaces the previous token while retaining its family. Logout and admin disable/delete revoke applicable sessions.
- Device lifecycle: `Active` can use configuration/license; `Disabled` remains known and forbidden; `Deleted` is a soft-deleted registration and may enter re-registration.
- `DeviceController`: pending-session `registration/current` and `device/request`. Requests use stable installation `deviceId`, machine name, physical address and HonestFlow version.
- `AdminController`: admin-key operations for clients, devices, registrations, licenses, versions/assets, overrides, integration settings and support requests. Approve creates or reactivates a Deleted Device and links matching pending refresh sessions; Reject keeps the session restricted.
- `ConfigurationController`: active-device `configuration/current`; combines Client, Device, ClientSettings, LicensePolicy, global versions, per-client overrides and assets. Effective component version is `client override ?? global version`.
- `LicenseController`: active-device `license/current`; returns the newest active verified `PersonalGrant`, supports ETag/304, and distinguishes missing/revoked/expired grants.
- `AssetsController`: protected download redirects/metadata resolution.
- `SupportController`, `ConnectionRequestsController`, `VersionController`: support creation, public connection requests and public current versions.
- License publication: HonestDesk signs exact grant bytes; `LicenseSignatureVerifier` checks the signature against configured public keys before storage. Never parse/reserialize signed bytes before verification or delivery.

## SQLite database

- Production database name: `HonestLicenseFull.db`.
- Production path: `/opt/honestserver/HonestLicenseFull.db`.
- Local copies may exist for development, migration or diagnostics, but the production DB is external deployment state and is not required to exist in any repository checkout.
- Never infer production state from a local `.db`, and never bundle/overwrite production DB during code deployment.

Main tables/entities from `HonestDbContext`:

- `Clients`, `Credentials`, `ClientSettings` — customer identity, password hashes and integration settings.
- `Devices` — stable external DeviceId, name/address/comment and Active/Disabled/Deleted status.
- `DeviceRegistrationRequests` — Pending/Approved/Rejected registration workflow and requested metadata.
- `RefreshTokens` — access/refresh hashes, token families, expiry, rotation/revocation and pending-device linkage.
- `Licenses`, `LicensePolicies` — immutable signed grant history/current status and effective policy metadata.
- `AppVersions`, `ClientComponentVersions`, `ComponentAssets` — global versions, client overrides and downloadable component metadata.
- `SupportRequests`, `ConnectionRequests` — support and website connection requests.
- `AuditEvents` — administrative state-change audit records.

Schema initialization/update is in `Data/DatabaseSchema.cs`; integration tests use temporary SQLite databases through `tests/HonestLicenseServer.IntegrationTests/ApiFactory.cs`.

## Production topology

- Root: `/opt/honestserver`.
- Binary: `/opt/honestserver/HonestLicenseServer`.
- Database: `/opt/honestserver/HonestLicenseFull.db` (WAL/SHM may appear alongside it).
- systemd unit: `honestserver`; unit template: `deploy/honestserver.service`.
- API process listens behind Caddy on `127.0.0.1:5498`.
- Public API: `https://api.honestflow.ru`.
- Reverse proxy examples: `deploy/Caddyfile.example`; Caddy also routes `/api/*` on `honestflow.ru`.
- Environment/secrets are external (`/etc/honestserver/honestserver.env` in the service template), not repository content.

## Deployment

`C:\Work\HonestLicenseServer\deploy-production.ps1` is the production deployment entry point.

- `-Mode Code`: deploy executable/code only; production DB must remain untouched.
- `-Mode Database`: code plus an expected additive schema change; requires an explicit `-ExpectedColumn Table.Column`, validates schema, creates backups and uses guarded stop/start/rollback behavior.
- `-ValidateOnly` performs validation without deployment; `SupportsShouldProcess`/confirmation is intentional.
- Publish output is `publish-linux/`; it must never contain production DB, secrets or private keys.
- Never run publish, deploy, SSH/SCP, systemd or production DB operations unless the user explicitly requests that exact action and target.

## Rules for Codex

1. Always read this `AGENTS.md` first, then inspect only task-related files.
2. Do not scan an entire repository when the map below identifies the owning area.
3. Preserve the WPF production path; do not implement HonestFlow 3.0 UI in legacy WinForms.
4. Do not change runtime code, public contracts, DB schema or security semantics without a confirmed reason.
5. For a local bug, make the smallest coherent fix; do not perform architectural or unrelated refactoring.
6. Do not commit, push, publish, deploy, SSH or touch production unless explicitly commanded.
7. Never expose or move private signing keys, admin keys, passwords, bearer/refresh tokens, SMTP/Yandex secrets or protected local state.
8. Distinguish server source-of-truth state from UI collections, caches, published grants and local DB copies.
9. Preserve soft-delete/history, token rotation/revocation and signature-verification boundaries.
10. Before changing an API contract, inspect server controller/contracts/OpenAPI and both consumers; update API docs when public behavior changes.

## Where to look first

| Task | Start here |
| --- | --- |
| HonestFlow startup/login/restricted UI | `HonestFlow.WpfPrototype/StartupWindow.xaml*`, `Application/Bootstrap/`, `Infrastructure/Api/` |
| HonestFlow main UI | `HonestFlow.WpfPrototype/MainWindow.xaml*` |
| Device registration client flow | `Application/Licensing/DeviceRegistration*`, `Infrastructure/Licensing/ApiDeviceRegistrationRequestSender.cs`, `Infrastructure/Api/ApiDeviceRegistrationStatusProvider.cs` |
| License decision/download/cache | `Application/Licensing/`, `Infrastructure/Licensing/`, `Infrastructure/Configuration/` |
| HonestDesk startup stability | `HonestDesk/App.xaml.cs`, `MainWindow.xaml.cs`, `Services/AppLogger.cs`, `SettingsService.cs`, `StateService.cs` |
| HonestDesk Client/Device UI | `AdminEditorWindow.xaml*`, `OperatorDevicesWindow.xaml*`, `Infrastructure/Api/HonestLicenseAdminApiClient.cs` |
| Registration Approve/Reject | HonestDesk API client/windows; server `DeviceController.cs`, `AdminController.cs`, integration tests |
| License issue/publish/revoke | HonestDesk `Infrastructure/Licensing/`, `LicenseManagerWindow.xaml*`; server `LicenseController.cs`, `AdminController.cs`, `LicenseSignatureVerifier.cs` |
| Auth/tokens/403 | server `AuthController.cs`, `Authentication/`, `Data/TokenHelper.cs`; client `ApiSessionService.cs`, `ApiAuthService.cs` |
| Configuration/version/assets | server `ConfigurationController.cs`, `VersionController.cs`, `AssetsController.cs`; HonestFlow configuration/API infrastructure |
| DB entity/schema issue | `HonestDbContext.cs`, `DatabaseModels.cs`, `DatabaseSchema.cs`, integration tests; never start from production DB |
| Support requests | HonestFlow help/support workflow; HonestDesk `MainWindow.xaml.cs` and API client; server `SupportController.cs`, `AdminController.cs` |
| API contract change | server `Contracts/`, controller, `openapi/honest-license-v1.json`, generated client, HonestFlow/HonestDesk consumers |
| Ubuntu/deployment | `deploy-production.ps1`, `deploy/honestserver.service`, `deploy/DEPLOY-UBUNTU.md`, `deploy/Caddyfile.example` |
| Load/performance | `C:\Work\HonestLicenseServer\load-tests/` |
