# AGENTS.md

Guidance for coding agents. Shisui is a Windows/macOS network configuration app
(see [README.md](README.md), [DESIGN.md](DESIGN.md), [docs/agents/project-overview.md](docs/agents/project-overview.md)).
**Language**: Japanese for UI, comments, commit messages, and README; this AGENTS.md is in English.

## Build & Test

Before building or testing, read [docs/agents/build-test-commands.md](docs/agents/build-test-commands.md).
Basics: `dotnet build Shisui.slnx`, then `dotnet test Shisui.slnx --no-build`.

- Tests run in parallel per method (`Parallelize`); never depend on static state, shared files, or environment variables.
- Native AOT is repository-wide: register new serialized models in `ShisuiJsonContext`; verify with `pwsh scripts/release-local.ps1 -SkipUpload`.
- **UI verification**: launch the Windows app with an administrator token, exercise the changed UI, and check stdout/stderr for Avalonia binding errors. A live process alone is not verification. Do not remove the elevation block to smoke-test ([DESIGN.md](DESIGN.md#起動)).
- **Startup and migration**: preserve `asInvoker`, Velopack hooks before migration, elevation, and `SingleInstanceGuard`; keep the product AppUserModelID disabled in Debug. Preserve the checks in [DESIGN.md](DESIGN.md#インストール移行の信頼境界).

## Architecture

Before touching an area (commands, DI, DNS, optimization, NIC, uplink, diagnostics, adapters, UI, settings), read the matching section via [docs/agents/architecture.md](docs/agents/architecture.md).

## Key Conventions

- Service methods use the `Async` suffix and propagate `CancellationToken`.
- `*CommandBuilder` / `*Catalog` are pure (no `Process`/OS calls); OS access belongs to services and executors.
- Settings persistence ([DESIGN.md](DESIGN.md#設定と更新)): report save failures separately from completed OS changes; roll back update preferences on save failure.
- macOS: verify on real hardware before shipping.

Details: [docs/agents/key-conventions.md](docs/agents/key-conventions.md).

## Auto-update & Release

Design: [DESIGN.md](DESIGN.md#設定と更新). Before changing updates, packaging, signing, or workflows, read [docs/agents/auto-update-release.md](docs/agents/auto-update-release.md); to run a release, read [docs/agents/release-process.md](docs/agents/release-process.md).

- Change versions only through `/vava`; never hand-edit `Directory.Build.props` `<Version>`.
- Keep `Velopack` and the `$VpkVersion` pin in sync.
- Publish the Sigilune-signed PerMachine MSI, never the PerUser Setup.exe.
- Keep `Shisui.UI.exe` and its assembly name (`avares://Shisui.UI/...`).
- Keep the legacy `shisui.nephilim.jp` endpoint until 2027/05/31; redirect only `/`.
