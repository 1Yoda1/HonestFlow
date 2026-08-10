# AGENTS.md

## Purpose

HonestFlow is a Windows x64 WinForms engineering application for
installation, diagnostics, recovery, remote support, updating and
licensed access around Honest Mark/Честный Знак components. The main
project targets `net10.0-windows`, uses WinForms, and is published as a
single framework-dependent executable with the .NET 10 Desktop Runtime
supplied separately.

These rules are optimized for correct, minimal changes and low Codex
token/agent usage.

## Active branch context

These instructions were prepared against
`agent/granular-license-access`. Do not assume the default branch
contains the same licensing architecture. Always preserve the currently
checked-out branch unless the user explicitly asks to switch/create
branches.

## Architecture map

Start with the narrowest relevant area: - `Program.cs` --- startup
composition, single-instance behavior, device identity, bootstrap,
seller authentication, license observation, main UI and updater
handoff. - `Forms/` --- WinForms UI. - `Application/Auth/` --- seller
authentication and license-aware login flows. - `Application/Bootstrap/`
--- runtime composition and remote/local mode selection. -
`Application/Core/` and `Application/DeviceIdentity/` --- application
contracts and stable installation identity. - `Application/Diagnostics/`
--- logs, snapshots, archives and diagnostic submission. -
`Application/Installation/` --- component
detection/planning/install/reinstall logic. -
`Application/RemoteAccess/` --- RuDesktop/support workflows. -
`Infrastructure/` --- Windows, networking, configuration, licensing,
updates and external adapters. - `Models/` --- application data
models. - `HonestFlow.Tests/` --- xUnit tests. -
`HonestFlow.LicenseSigning/` --- signing-related support project; never
infer that private production keys belong in the client. -
`HonestFlow.WpfPrototype/` --- prototype, not the production WinForms UI
unless the task explicitly targets it. - `LEGACY_UI_SNAPSHOT.md` ---
historical UI reference only.

Do not scan the entire repository for a local UI/text change. Locate the
owning form/service first.

## Product invariants

Do not change these without explicit user direction: 1. HonestFlow is an
administrative Windows x64 application and may require elevation for
service/install operations. 2. It coordinates official component
installers; it does not replace their installation logic with home-grown
installation mechanisms. 3. Stable `deviceId` is an installation GUID
persisted under ProgramData and protected according to the current
implementation; do not turn it into a hardware fingerprint. 4. The
private ECDSA license-signing key must never be embedded in HonestFlow.
5. HonestFlow verifies signed license/grant data locally using trusted
public keys. 6. Preserve exact signed bytes during signature
verification. Do not reserialize signed JSON before verification. 7.
Offline operation is allowed only from previously verified cached
license data and only within signed policy/expiry limits. 8. Feature
access must continue to respect granular license permissions; do not
bypass authorization to make UI/actions easier. 9. Do not silently fall
back from a failed security check to unrestricted access. 10.
Secrets/tokens/passwords must not be written to logs. Preserve
masking/redaction behavior. 11. Keep installer/update integrity checks
(size/hash/signature where currently used); do not weaken them for
convenience. 12. Do not convert the project to Native AOT,
self-contained deployment, trimming, another UI framework, or another
target architecture unless explicitly requested.

## Licensing/API work

HonestFlow consumes HonestLicenseServer. Before changing login, refresh,
device registration, configuration, asset, or license behavior, inspect
the existing client contracts/services and the server contract
assumptions. Do not invent server routes or JSON shapes from memory.

A license failure, disabled client/device, pending device, expired
offline grant, minimum-version failure, or missing feature permission
are distinct states. Preserve their semantics instead of collapsing them
into a generic success/failure path.

When a change requires a server contract change, state it explicitly; do
not silently hard-code a client-only workaround.

## Windows/service/installer work

Service manipulation, process termination, registry access, MSI/EXE
execution, file replacement and ProgramData writes are operationally
sensitive. Reuse existing helpers/services and logging patterns.
Preserve cancellation/timeouts where present. Do not broaden
process/service kills beyond known product processes. Do not delete
user/client data as a generic repair strategy.

For LM ЧЗ/Regime, controller, ESM/ТС ПИоТ, KKT/ATOL and RuDesktop
diagnostics, prefer detection and explicit state reporting before
destructive repair.

## UI changes

The production UI is WinForms unless the task explicitly targets
`HonestFlow.WpfPrototype`. Preserve current application flow and
existing async behavior. Do not block the UI thread with network,
install, diagnostics, or service polling work. Keep user-facing Russian
text consistent with nearby UI.

Do not redesign unrelated screens while implementing a functional
change.

## Agent efficiency rules

1.  Read the task and identify one owning subsystem before searching.
2.  Inspect direct callers/callees only as needed.
3.  Make the smallest coherent change.
4.  Do not run `dotnet restore`, `dotnet publish`, installer creation,
    packaging, Git commit/push, release creation, deployment, or remote
    server operations unless explicitly requested.
5.  Do not launch HonestFlow interactively unless explicitly requested
    and the environment supports it.
6.  Do not build after every edit.
7.  Small/local change: leave build to the user.
8.  Medium cross-file change: static inspection plus targeted tests is
    preferred.
9.  Large startup/licensing/installer/refactor change: one build/test
    pass is reasonable when it materially validates integration.
10. If build/test fails, fix the concrete task-related failure only; do
    not start repository-wide cleanup.

The user normally performs build/publish/Git/release steps separately.
Save agent cycles for code reasoning and implementation.

## Validation

Use the narrowest useful validation. For logic covered by tests, prefer
targeted `dotnet test` filters/project tests over full solution work. A
full test/build pass is appropriate mainly for cross-cutting changes.

Do not alter production configuration or contact real client endpoints
merely to validate a code change.

## Code style

Follow surrounding C# conventions; this codebase includes older
nullable-disabled code, so do not mass-convert nullability/style as part
of a feature. Prefer existing abstractions and helpers over duplicate
services. Avoid new dependencies unless clearly justified. Avoid
drive-by refactors and formatting churn.

## Cross-repository awareness

-   Backend/API: `1Yoda1/HonestLicenseServer`.
-   Admin/license publisher: `1Yoda1/HonestDesk`.

When changing a shared contract or license meaning, state the
cross-repository impact. Do not edit another repository without explicit
authorization.

## Completion format

Report concisely: behavior implemented; files changed; validation
performed/not performed; security/license/API impact; exact next command
for manual validation if useful. Never commit, push, publish, package,
deploy, or release unless explicitly requested.
