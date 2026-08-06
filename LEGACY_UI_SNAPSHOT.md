# HonestFlow WinForms UI snapshot

Baseline: `61e401e` (`Complete MainForm workflow extraction`). This document is the parity checklist for the WPF migration. The WinForms implementation remains in `Forms/` until every item below is implemented and smoke-tested in WPF.

## Startup and authentication

| Surface | User action | Handler / workflow | Required WPF behavior |
|---|---|---|---|
| Startup progress | Automatic startup | `Program.StartApplicationAsync`, `ApplicationStartupService` | Initialize logging and cache locations, obtain `DeviceId`, load remote/local configuration, report progress |
| Startup authentication | Enter seller password | `SellerAuthenticationWorkflow.AuthenticateAsync` | Resolve client and immediately observe license |
| Remembered seller | Confirm remembered client | `Program.TryRestoreRememberedSellerAsync` | Validate remembered client and password fingerprint, refresh license before entry |
| Authentication fallback | Enter another password | `StartupProgressForm.Authentication` | Return to password entry without restarting |
| Diagnostic fallback | Diagnostics/help without authorization | `StartupProgressForm`, restricted `MainForm` mode | Replaced by registration/help screen when device is not licensed; diagnostics must remain available from support flow |

## Main commands

| Legacy control | Caption / purpose | Handler | Extracted dependency |
|---|---|---|---|
| `button2` | Seller sign-in | `Button2_Click` | `SellerAuthenticationWorkflow` |
| `btnStartInstallation` | Start/cancel installation | `BtnStartInstallation_Click` | `ComponentInstallationWorkflow` |
| `btnCheckWithoutPassword` | Refresh status / request help in restricted mode | `BtnRefreshStatus_Click`, `BtnRequestHelp_Click` | `PointStatusRefreshService`, `HelpRequestWorkflow` |
| `btnDiagnostics` | Collect and send diagnostics | `BtnDiagnostics_Click` | `DiagnosticWorkflowService` |
| `btnReinstallComponents` | Select and reinstall components | `BtnReinstallComponents_Click` | `ComponentInstallationWorkflow` |
| `btnRestoreLmDatabase` | Restore LM database | `BtnRestoreLmDatabase_Click` | `MaintenanceWorkflow` |
| `btnMaintenance` | Maintenance action chooser | `BtnMaintenance_Click` | `MaintenanceWorkflow`, `MainFormDialogService` |
| `btnOpenKktDriver` | Open KKT driver | `BtnOpenKktDriver_Click` | `ExternalApplicationLauncher` |
| `btnOpenEsm` | Open ESM | `BtnOpenEsm_Click` | `ExternalApplicationLauncher` |
| `btnDetails` | Version/configuration details | `BtnDetails_Click` | Current session data |
| `btnRateApplication` | Send application rating | `BtnRateApplication_Click` | `AppRatingEmailSender` |
| `btnRefreshLicense` | Refresh license | `BtnRefreshLicense_Click` | `LicenseRefreshWorkflow` |
| `btnPointStatusDetails` | Full point-status report | `ShowPointStatusDetails_Click` | `PointStatusReportBuilder` |

## Point topology and dynamic actions

The dashboard exposes LM, controller, ESM, KKT, cloud and RuDesktop nodes. Every node preserves its title, detailed status, severity, action caption and action kind from `NodeStatus`.

Dynamic actions that must remain available:

- refresh all statuses;
- start/restart/stop Windows services;
- recover LM services;
- initialize LM;
- install or configure RuDesktop;
- request remote help;
- show node details and the full point-status report;
- show installed and required component versions;
- disable actions while another long-running operation is active;
- support cancellation of installation.

## License-driven behavior

- Authenticate first, then evaluate the license for the resolved `ClientId + DeviceId`.
- `Allowed`: open the main window and apply feature permissions.
- `DeviceNotRegistered`: automatically submit one registration request and show only the support action instead of disabled feature cards.
- Other denied states: do not expose system-changing actions; keep support/diagnostic recovery available where policy permits.
- React to `LicenseObservationSnapshotStore.SnapshotChanged` and refresh UI/status after license changes.
- Preserve periodic license refresh and manual refresh.

## Dialogs and secondary flows

- component selection for reinstall;
- maintenance action selection;
- diagnostic log selection;
- help request form and delivery result;
- point address capture/synchronization;
- RuDesktop password setup;
- confirmation for dangerous operations;
- inline success, warning and error notifications;
- license progress and device-registration delivery status.

## State and lifecycle

- selected/authorized client and remembered authorization;
- remote versus protected offline configuration;
- latest point-status result;
- current license snapshot and feature permissions;
- single-instance protection;
- self-update after startup;
- cancellation tokens for window lifetime and installation;
- logger initialization, flush and shutdown;
- no duplicate event subscriptions when node actions change.

## Removal gate for WinForms

Do not remove `Program.cs`, `Forms/MainForm*`, `StartupProgressForm*`, `SellerLoginForm`, `LicenseCheckProgressForm` or `ServiceMenuForm` until WPF passes this checklist, all automated tests, and manual online/offline/license/install/diagnostic/update smoke tests.
