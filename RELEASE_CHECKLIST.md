# HonestFlow Release Checklist

Use this checklist before creating a public GitHub release.

## Source State

- [ ] Release branch is intentional.
- [ ] `git status` is clean.
- [ ] Version in `HonestFlow.csproj` matches the release tag.
- [ ] No runtime configuration, logs, diagnostics, installer caches, or secrets are staged.

## Validation

- [ ] `dotnet restore`
- [ ] `dotnet build HonestFlow.csproj -c Release`
- [ ] `dotnet test HonestFlow.Tests\HonestFlow.Tests.csproj -c Release`
- [ ] Target framework support status is accepted for this release. `net6.0-windows` is out of support and should be upgraded before a broad public release.
- [ ] Published executable starts on a clean Windows x64 machine.
- [ ] UI text is readable in Russian.

## Smoke Tests

- [ ] First launch with internet.
- [ ] First launch without internet.
- [ ] Missing local config files.
- [ ] Corrupted local config files.
- [ ] UAC cancellation.
- [ ] MSI busy state, such as Windows Update or another installer running.
- [ ] Missing installer files in local mode.
- [ ] Interrupted download.
- [ ] Reboot-required installer exit code.
- [ ] Self-update success and rollback.
- [ ] Diagnostic archive collection and sending.

## GitHub Release

- [ ] Release notes explain what changed.
- [ ] Assets are attached.
- [ ] Checksums are published.
- [ ] Known limitations are listed.
- [ ] Security-sensitive runtime files are not attached.
