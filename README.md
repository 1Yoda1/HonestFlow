# HonestFlow

HonestFlow is a Windows desktop tool for preparing, checking, and supporting client workstations that use Honest Sign related components.

The application helps an engineer install required components, verify local services and APIs, collect diagnostics, request support, and keep client configuration up to date.

## What It Does

- Checks the workstation state: LM, controller, ESM, KKT, cloud connectivity, and remote access.
- Installs or repairs supported components from local or remote installer caches.
- Supports remote configuration through Yandex Disk public resources.
- Enforces feature access through a signed license manifest.
- Collects a diagnostic archive for support.
- Can update HonestFlow itself when a newer published build is available.

## Requirements

- Windows x64.
- .NET 6 Desktop Runtime to run HonestFlow.
- Administrator rights for installation, repair, Windows service control, MSI execution, and runtime installation.
- Network access to configured Yandex Disk resources for remote configuration, updates, and installer downloads.

## Repository Structure

```text
Application/                    Application workflows and use cases
Application/Licensing/           License decisions, access policy, observation snapshots
Application/PointStatus/         Workstation health checks
Application/Installation/        Component installation planning and orchestration
Application/Diagnostics/         Diagnostic archive collection and delivery
Application/RemoteAccess/        RuDesktop installation and support flows
Forms/                           WinForms UI
Infrastructure/                  File system, network, process, installer, logging, and DPAPI adapters
Models/                          Data models and DTOs
HonestFlow.Tests/                xUnit tests
HonestFlow.LicenseSigning/       License manifest signing helper
Resourses/                       Application icon
```

## Build And Test

```powershell
dotnet restore
dotnet build HonestFlow.csproj -c Release
dotnet test HonestFlow.Tests\HonestFlow.Tests.csproj -c Release
```

## Publish

```powershell
dotnet publish HonestFlow.csproj -c Release -r win-x64
```

The project is configured as a single-file, framework-dependent Windows executable. The published `HonestFlow.exe` expects the required .NET Desktop Runtime to be available on the target machine.

Note: the current project target is `net6.0-windows`. .NET 6 is out of support; upgrade the target framework before a broad public release unless the deployment environment explicitly requires .NET 6.

## Runtime Configuration

HonestFlow can use local files next to the executable and remote files from the configured Yandex Disk public folder.

Sensitive runtime files must not be committed:

- `ips_encrypted.json`
- `support_mail_encrypted.json`
- `yandex_public_key.txt`
- `yandex_public_url.txt`
- `licenses.json`
- `licenses.json.sig`
- installer caches, diagnostics, logs, and local DPAPI state

Important: files named `*_encrypted.json` are compatibility-obfuscated, not cryptographically protected secrets. Treat them as sensitive production configuration.

## Logs And Diagnostics

Runtime logs and diagnostic archives are stored under:

```text
%ProgramData%\HonestFlow
```

Diagnostic archives may contain workstation names, Windows usernames, device identifiers, point addresses, and product logs. Review sensitive data handling before sharing diagnostics outside the support boundary.

## Release Checklist

Before publishing a public release:

- Ensure `git status` is clean.
- Ensure `HonestFlow.csproj` version matches the GitHub release tag.
- Run tests in Release configuration.
- Build and smoke-test the published executable on a clean Windows machine.
- Test offline startup, missing config files, UAC cancellation, MSI busy state, update rollback, and reboot-required flows.
- Publish release notes, checksums, and the intended installer assets.

## Status

HonestFlow is in active development and is currently optimized for controlled operational use. Public releases should include explicit setup instructions, known limitations, and security notes.

## Author

Development and maintenance: Pavel Shadrov.
