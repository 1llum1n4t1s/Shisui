# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Shisui is a cross-platform (Windows / macOS) desktop app for network configuration. It lets the user switch
DNS providers (Cloudflare standard / malware-block / malware+adult-block, Google Public DNS, or custom IPv4/IPv6),
flush the DNS cache, and — on Windows only — toggle BBR2 congestion control and run a catalog of `netsh` /
`ipconfig` / `nbtstat` network maintenance commands.

**Language**: Japanese (UI, comments, commit messages, README are all in Japanese). This CLAUDE.md is in English
to match the reference project's documentation conventions; code comments and user-facing text remain Japanese.

Architecture mirrors `RealTimeTranslator` (`C:\Users\IMT\dev\RealTimeTranslator`): Avalonia + CommunityToolkit.Mvvm
UI, `Core`/`UI`/`Tests` project split, manual `ServiceCollection` DI in `App.axaml.cs`. Unlike RealTimeTranslator
(Windows-only), Shisui targets both Windows and macOS, so `TargetFramework` is the platform-neutral `net10.0`
(not `net10.0-windows...`), and OS-specific services live under `Core/Services/Windows` and `Core/Services/MacOS`,
selected at DI-registration time via `OperatingSystem.IsWindows()` / `OperatingSystem.IsMacOS()`.

## Build & Test Commands

```bash
dotnet build Shisui.slnx
dotnet test Shisui.slnx --no-build
dotnet run --project src/Shisui.UI

# Run a single test class / method (MSTest via VSTest filter)
dotnet test Shisui.slnx --no-build --filter "FullyQualifiedName~WindowsDnsCommandBuilderTests"
dotnet test Shisui.slnx --no-build --filter "FullyQualifiedName~WindowsTcpStateParserTests.Parse_AllTemplatesBbr2_ReturnsEnabled"

# Local signed release (see "Auto-update & Release"). -SkipUpload = build + sign only, no cloud.
pwsh scripts/release-local.ps1 -SkipUpload
```

No RID-locked `packages.lock.json` is used, so there is no `--no-restore` dance for local builds. The release
script (`scripts/release-local.ps1`) does its own RID-locked self-contained publish; it does not touch a lockfile.

**Verifying the UI when it can't be automated**: the app ships with `requireAdministrator` in `app.manifest`, so
UAC's secure desktop blocks screenshot/computer-use driving. The established smoke-test loop is: flip
`app.manifest` to `asInvoker`, `dotnet build src/Shisui.UI`, launch the built exe, confirm the process stays up
with an empty stdout/stderr (Avalonia writes binding errors there), then restore `requireAdministrator` and
rebuild. Compiled bindings (`x:DataType`) catch binding-path typos at build time, which is the main safety net.

## Architecture

### Project Structure

- **Shisui.Core** — Interfaces, models, and all OS-interacting services. No UI dependency.
  - `Interfaces/` — `INetworkAdapterService`, `IDnsConfigurationService`, `IDnsCacheService`, `ITcpTuningService`,
    `INetworkMaintenanceService`, `ICommandExecutor`, `ISettingsService`.
  - `Models/` — `NetworkAdapterInfo`, `DnsServerSet`, `DnsProviderPreset`, `DnsPresetCatalog` (hardcoded official
    Cloudflare/Google IP addresses), `MaintenanceCommandDefinition`, `CommandExecutionResult`, `AppSettings`.
  - `Services/Windows/` — netsh/ipconfig-backed implementations. Command *building* (pure string formatting) is
    split from command *execution* (`ICommandExecutor`) so the exact command strings are unit-testable without
    touching the OS. See `WindowsDnsCommandBuilder`, `WindowsTcpCommandBuilder`, `WindowsMaintenanceCommandCatalog`.
  - `Services/MacOS/` — `networksetup`/`dscacheutil`-backed implementations, plus `MacElevatedCommandExecutor`
    which wraps every command through `osascript -e 'do shell script "..." with administrator privileges'`
    (macOS apps should not request a blanket admin launch the way Windows apps do via manifest).
- **Shisui.UI** — Avalonia desktop app. Views/ViewModels (CommunityToolkit.Mvvm `[ObservableProperty]` /
  `[RelayCommand]`), DI setup in `App.axaml.cs`.
- **Shisui.Tests** — MSTest unit tests. Covers command builders and the DNS preset catalog's exact IP values —
  deliberately does **not** try to exercise the real `ICommandExecutor` (that requires admin rights and mutates
  the host's real network config).

### Command execution model — string arguments, not `ArgumentList`

`ICommandExecutor.RunAsync(string fileName, string arguments, ...)` takes a single pre-formatted argument
**string**, not `ArgumentList`/`string[]`. This is deliberate: `netsh` does not use standard `CommandLineToArgvW`
argv parsing — it re-parses the raw command line itself and expects literal `name="Adapter Name"` quoting.
Passing adapter names through .NET's `ProcessStartInfo.ArgumentList` causes the runtime to escape embedded quotes
(`\"`), which breaks netsh's own tokenizer. Each `*CommandBuilder` (Windows and macOS) is responsible for quoting
correctly for its target executable's own parsing convention (verified empirically: `netsh int tcp set global
default` and `route /f` were confirmed against this machine / official docs during initial implementation, see
git history / commit description for the empirical verification).

On macOS, `MacElevatedCommandExecutor` re-wraps the already-quoted `fileName + " " + arguments` shell command
inside an AppleScript string literal (backslash/quote escaping only) and invokes `osascript` via `ArgumentList`
internally (a normal argv-parsing tool, so `ArgumentList` is correct there — this is the one place in the codebase
mixing both invocation styles, intentionally).

### DI: Windows-only features are optional dependencies, not stubbed

`ITcpTuningService` and `INetworkMaintenanceService` are only registered in `App.axaml.cs` when
`OperatingSystem.IsWindows()`. `MainWindowViewModel`'s constructor takes `TcpTuningViewModel? = null` and
`MaintenanceViewModel? = null` with explicit default values — `Microsoft.Extensions.DependencyInjection` only
substitutes `null` for an unregistered service type when the constructor parameter has a default value; without
`= null` it throws `InvalidOperationException` at startup on macOS. The corresponding tabs in `MainWindow.axaml`
are gated on `IsVisible="{Binding IsWindows}"`, not on null-checking the ViewModel directly.

### DNS Preset Catalog (`Core/Models/DnsPresetCatalog.cs`)

Hardcoded official addresses — do not "correct" these without checking the provider's current documentation:

| Preset | IPv4 | IPv6 |
|---|---|---|
| Cloudflare 標準 | 1.1.1.1 / 1.0.0.1 | 2606:4700:4700::1111 / ::1001 |
| Cloudflare マルウェアブロック | 1.1.1.2 / 1.0.0.2 | 2606:4700:4700::1112 / ::1002 |
| Cloudflare マルウェア+アダルトブロック | 1.1.1.3 / 1.0.0.3 | 2606:4700:4700::1113 / ::1003 |
| Google Public DNS | 8.8.8.8 / 8.8.4.4 | 2001:4860:4860::8888 / ::8844 |

### Reading current state is locale-independent (PowerShell cmdlets, NOT netsh text)

`netsh int tcp show global` / `show supplemental` values are stable English tokens (`enabled` / `bbr2`) but the
**labels are localized** (Japanese Windows shows 「Receive-Side Scaling 状態」 etc.), so parsing that text breaks
on non-English Windows. So the "current state" badges in the BBR2/TCP tab read state through
`WindowsTcpStateCommandBuilder` → a one-line PowerShell command that emits `KEY=VALUE` lines from
`Get-NetOffloadGlobalSetting` (RSS/RSC) + `Get-NetTCPSetting` (ECN/Timestamps/CongestionProvider). The keys are
English-fixed by us and the enum values are English, so the whole output is locale-independent;
`WindowsTcpStateParser` (pure, unit-tested) parses it. **BBR2 status = all 5 templates' `CongestionProvider`**
(Enabled / Partial / Disabled). Two things are deliberately *not* in the badge: **FastOpen** (no PowerShell
property exists → shown as 「取得非対応」) and **loopbacklargemtu** (only in fully-localized netsh output, unreadable
locale-independently → the BBR2 enable button still sets it, but its live state isn't tracked).

### Adapter list filtering (`WindowsNetworkAdapterFilter`, pure)

`NetworkInterface.GetAllNetworkInterfaces()` returns real adapters plus a lot of noise: NDIS filter/binding
**child interfaces** (`<parent>-QoS Packet Scheduler-0000`, `<parent>-WFP ...Filter-0000`), Wi-Fi Direct virtual
adapters, and `NotPresent` pseudo-devices. The DNS dropdown must match what `ncpa.cpl` shows. The pure filter keeps
Ethernet/Wireless80211 that are Present, drops a "binding child" if its Description is `<another adapter's
Description>-...` (structural — works for unknown antivirus/VPN LWFs without a hardcoded name list), and drops
`Wi-Fi Direct Virtual Adapter` by its (locale-stable, English) driver description. VPN TAP / Hyper-V vEthernet stay.

### Ghost (disconnected) network device cleanup (`IGhostAdapterService`, Windows)

A section in the DNS tab lists **disconnected** network-class devices (leftover registry entries from removed USB
adapters / uninstalled VPNs) via `pnputil /enum-devices /disconnected /class Net /format xml` (XML for
locale-independent parsing) and removes one via `pnputil /remove-device "<InstanceId>"`. Windows' own WAN Miniport
virtual devices match the same filter, so `WindowsGhostAdapterParser` flags `Manufacturer` containing "Microsoft"
(→ UI shows a ⚠ warning) and **every** row requires its own per-row 確認済み checkbox before delete. This uses
pnputil's proper PnP uninstall path rather than raw registry edits.

### UI Framework

- **Avalonia 12.0.5** with Fluent theme, matching RealTimeTranslator's version.
- MVVM via CommunityToolkit.Mvvm 8.4.2. Compiled bindings (`AvaloniaUseCompiledBindingsByDefault=true`) — every
  `.axaml` needs `x:DataType`; binding-path typos become build errors.
- **Design system is a copy of `C:\Users\IMT\dev\Lhamiel`'s "macOS Tahoe" look**: `Resources/Themes.axaml` holds
  the Light/Dark palette (`Color.*` / `Brush.*`, Apple-blue accent, translucent glass `Brush.Container`), and
  `App.axaml` holds the shared styles — a **left-sidebar `TabControl.sidebar` ControlTemplate** (floating rounded
  glass panel), the `HeaderedContentControl` glass-card template used for every settings section, and unified
  corner radii. The window uses `TransparencyLevelHint="AcrylicBlur"` + `ExperimentalAcrylicBorder`. When adding UI,
  reuse `HeaderedContentControl` for cards and the `h1`/`caption` TextBlock classes rather than inventing styles.
- Main view: `MainWindow` is a sidebar `TabControl` — DNS 設定 / BBR2・TCP 調整 / メンテナンス (last two
  `IsVisible="{Binding IsWindows}"`) / バージョン — plus a persistent 実行ログ card fed by each tab ViewModel's
  `CommandExecuted` event, one `UserControl` per tab.
- Destructive maintenance commands require a per-row "確認済み" `CheckBox` before their `RunCommand` can execute
  (`MaintenanceCommandItemViewModel.CanRun`) — deliberately not a modal dialog, to keep everything on compiled
  bindings without a dialog-service abstraction.

### Settings & Logging

- Settings: plain `System.Text.Json` read/write in `SettingsService` (no `IOptionsMonitor`/hot-reload — unlike
  RealTimeTranslator, nothing here needs live config reload while a background pipeline runs). Path resolved by
  `AppPaths` per-OS convention: `%APPDATA%\Shisui\settings.json` (Windows) /
  `~/Library/Application Support/Shisui/settings.json` (macOS).
- Logging: `SuperLightLogger` NuGet package (same as RealTimeTranslator), via `LoggerBootstrap`. Logs to
  `AppPaths.LogsDirectory`.
- Single-instance: `SingleInstanceGuard` uses a `FileShare.None`-locked file under the OS temp directory, not a
  named `Mutex` — named mutexes with `Local\`/`Global\` prefixes are a Windows-only convention and behave
  differently (or not at all) cross-platform, so a plain file lock is used for portability instead.

## Key Conventions

- **Async**: service methods use `Async` suffix, propagate `CancellationToken`.
- **Command builders are pure functions**: no `Process`/OS calls inside `*CommandBuilder` / `*Catalog` classes —
  keeps them unit-testable. Only the `*Service` classes (annotated `[SupportedOSPlatform(...)]`) touch the OS.
- **macOS paths are implemented but unverified on real hardware**: this repo was built entirely on a Windows
  machine. The Windows command catalog was empirically tested (unit tests + a couple of live `netsh` invocations
  during development); the macOS `networksetup`/`osascript` code compiles and is written carefully against known
  command syntax, but has not been run on an actual Mac. Verify before shipping a macOS build.

## Auto-update & Release (Velopack + Cloudflare R2, Windows-only signed distribution)

Shisui ships as a **signed Velopack app distributed from Cloudflare R2** (Windows only; macOS distribution would
need separate Apple notarization and is not set up).

- **Update source**: `SimpleWebSource` pointing at **`https://shisui.nephilim.jp`** (R2 bucket `shisui-updates`,
  custom domain on the `nephilim.jp` Cloudflare zone). The base URL is hardcoded in `AppSettings.UpdateBaseUrl`
  with `[JsonIgnore]` (not overridable from settings.json — closes the third-party-host redirection attack surface).
  Channel is `win` only (`releases.win.json`).
- **Client wiring**: `Program.cs` calls `VelopackApp.Build().Run()` first (before the single-instance guard);
  `UI/Services/UpdateService.cs` wraps `UpdateManager(SimpleWebSource)` for check/download/apply; the バージョン tab
  (`VersionViewModel` / `VersionView`) shows the current version, a 更新を確認 button, and auto-checks on startup
  (gated by `AppSettings.CheckForUpdatesOnStartup`). In dev (`dotnet run`), `UpdateManager.IsInstalled` is false so
  the check no-ops — that is expected.
- **Release is local + signed, not CI**: `scripts/release-local.ps1` (adapted from `C:\Users\IMT\dev\VStoVSC`)
  does publish (self-contained win-x64) → `vpk pack` + **Authenticode sign** (Certum "Open Source Code Signing in
  the cloud", `signtool /n "Open Source Developer Yuichiro Shinozaki"`) → signature verify → R2 upload (wrangler) →
  Cloudflare cache purge → manifest-match distribution check → aggressive old-nupkg cleanup. Signing needs
  **SimplySign Desktop logged in** (token + phone OTP), which is why release is local, not GitHub Actions.
  `pwsh scripts/release-local.ps1 -SkipUpload` builds + signs only (no cloud) for verification.
- **`/vava` integration**: `vava.config.json` has a `localRelease` block so `/vava` runs cert precheck → version
  bump → `release-local.ps1` automatically. **Version bumps go through `/vava` only** — do not hand-edit
  `Directory.Build.props` `<Version>`.
- **No "bridge" GitHub Release needed**: unlike the sister apps migrated with `/transfer-cf`, Shisui was born on
  R2 (never had a `GithubSource` client), so there is no legacy client to rescue. GitHub Releases are unused.
- **App exe is `Shisui.UI.exe`** (AssemblyName left at default to avoid breaking `avares://Shisui.UI/...`
  resource URIs); the user-visible Start Menu / shortcut name is `Shisui` via `vpk pack --packTitle`.
  Cloudflare account `10901bfadbf1005164774a7350082985`, zone `ce5dc5c4ba535d7230f6003b0220bb99`.
