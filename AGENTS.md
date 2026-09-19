# AGENTS.md

This file provides guidance to coding agents working in this repository.

## Project Overview

Shisui is a cross-platform (Windows / macOS) desktop app for network configuration. It lets the user switch
DNS providers (Cloudflare standard / malware-block / malware+adult-block, Google Public DNS, Quad9, NextDNS, or
custom IPv4/IPv6), flush the DNS cache, run a Ping/Traceroute diagnostics tool, and — on Windows only — toggle
DNS over HTTPS (DoH) and DNS over TLS (DoT) for the selected preset, toggle BBR2 congestion control / TCP global
options (including RACK/TLP loss recovery, receive-window auto-tuning, UDP URO/USO restoration, and per-adapter
MTU restoration to 1500), apply and restore an allowlisted gaming-oriented low-latency profile for physical NICs,
run a catalog of `netsh` / `ipconfig` / `nbtstat` network maintenance commands, view read-only adapter details
(MAC address / link speed), and clean up disconnected "ghost" network devices. The diagnostics UI supports
4/30/100 Ping probes and, on Windows, reports loss, min/average/max, p95, and jitter.

**Language**: Japanese (UI, comments, commit messages, README are all in Japanese). This AGENTS.md is in English
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
Native AOT is enabled repository-wide through `Directory.Build.props`. When adding serialized application models,
register them in `ShisuiJsonContext`; reflection-based JSON paths can fail only at publish/runtime even when a normal
build succeeds. Use `pwsh scripts/release-local.ps1 -SkipUpload` to verify the actual win-x64 AOT distribution path.

**Verifying the UI when it can't be automated**: `app.manifest` is `asInvoker` (see below), but
`Program.cs` self-relaunches elevated via `WindowsElevationHelper` on every real startup, so UAC's secure
desktop still blocks screenshot/computer-use driving. The established smoke-test loop is: temporarily comment
out the `WindowsElevationHelper` relaunch block in `Program.cs`, `dotnet build src/Shisui.UI`, launch the built
exe, confirm the process stays up with an empty stdout/stderr (Avalonia writes binding errors there), then
restore the block and rebuild. Compiled bindings (`x:DataType`) catch binding-path typos at build time, which
is the main safety net.

**Why elevation happens at runtime, not via the manifest**: `app.manifest` requests `asInvoker`, not
`requireAdministrator`, even though almost every command this app runs (`netsh`/`ipconfig`/DNS changes) needs
admin. A `requireAdministrator` manifest breaks Velopack's installer: `Setup.exe`/`Update.exe` invokes the
packaged exe with internal hook args (`--veloapp-install` etc.) via `CreateProcess`, which cannot elevate —
only `ShellExecute` can — so the hook call fails immediately with `ERROR_ELEVATION_REQUIRED` (Win32 740,
`os error -2147024156`) and the installer aborts with a "Setup エラー" dialog. This is a confirmed Velopack
limitation, not a Shisui bug (maintainer: "Velopack does not support applications requiring admin at this
time" — https://github.com/velopack/velopack.docs/discussions/8). The fix: keep the manifest at `asInvoker` so
Velopack's own process launches succeed, and perform the actual elevation in `Program.cs` immediately after
`VelopackApp.Build().Run()` and the PerUser→PerMachine migration check — `WindowsElevationHelper.IsRunningAsAdministrator()` checks the current token, and
if not elevated, `TryRelaunchElevated` restarts the process via `ShellExecute` + the `runas` verb (one UAC
prompt at startup, matching the original design intent of not re-prompting per command) and exits the
non-elevated instance. This check runs *before* `SingleInstanceGuard` is acquired, so the non-elevated process
never holds the single-instance lock while its elevated replacement starts. Release builds also set Velopack's
`velopack.Shisui` process AppUserModelID before UI creation so the installed shortcut and elevated process share
one taskbar identity. Debug builds must not set that product AppUserModelID: Windows can otherwise resolve the
development EXE through the installed shortcut and show a blank taskbar icon instead of the EXE's embedded icon.
Debug builds still perform runtime elevation because TCP/DNS settings mutate system-wide state. To keep a
debugger attached while exercising those paths, start Visual Studio itself as administrator; a non-elevated IDE
causes the first process to relaunch elevated and detach from that original debugging session.

Windows releases are installed by the signed Velopack **PerMachine MSI** under protected `Program Files`; do not
publish the generated PerUser `Setup.exe`. Legacy `%LocalAppData%\Shisui` builds are the one-time exception:
`WindowsPerMachineMigration` runs before whole-app elevation, downloads `Shisui-win.msi` from the fixed R2 origin,
validates it with `ExecutableTrustVerifier` (WinVerifyTrust + expected publisher CN; ported from Lumin4ti's
hardened verifier — System32-only DLL loading so a planted `wintrust.dll` next to the user-writable legacy exe
can't be picked up by the elevated process, whole-chain revocation checks with `AuthenticodeRevocationMode.Online`
for downloaded installers / `CacheOnly` for local executables, and certificate extraction from the verified trust
chain rather than a second file read). The downloaded MSI is opened without write/delete sharing, verified through
that same handle, and kept locked until system `msiexec` exits, closing the verification-to-install TOCTOU window.
The installed Program Files build never executes the user-writable legacy `Update.exe`; it performs a bounded,
no-reparse-point cleanup directly, preserving `%APPDATA%\Shisui` settings/logs while removing the old executable
tree, package cache, HKCU uninstall entry, and per-user shortcuts. A trusted PerMachine build also detects the
exact legacy root on startup when the MSI was installed directly and no pending marker was created, including a
partial residue containing only `Update.exe` or `packages`. Cleanup is allowed only when the registered stable
executable, `.msi-installed` marker, current process location, and current process Authenticode publisher agree,
so it never treats its own executable tree as the legacy target. The fallback continues past individually locked
entries, while a pending marker and HKCU RunOnce retry whatever remains after transient cleanup locks.
Known malformed MSI locations (`C:\Shisui` and `Program Files\ゆろち\Shisui`) are repaired only when the current
process, registered stable executable, `.msi-installed` marker, and Authenticode publisher all agree. The repair
downloads the fixed signed MSI, installs it under `Program Files\Shisui`, and records the exact old root in an
administrator-writable HKLM marker before MSI execution. A same-ProductCode maintenance no-op falls back to
`REINSTALL=ALL REINSTALLMODE=vamus`; only the two fixed legacy roots can be deleted, and arbitrary custom machine
locations remain untouched.

## Architecture

現在の構造・責務・境界・不変条件の正本は [DESIGN.md](DESIGN.md)。実装領域ごとの詳細は
[references/architecture.md](references/architecture.md)。**下の領域を触る前に必ず該当節を読む**。

| 触る対象 | 読む節 |
| --- | --- |
| プロジェクト構成、どこに何があるか | Project Structure |
| 外部コマンド実行（PowerShell / netsh） | Command execution model — string arguments, not `ArgumentList` |
| DI 登録、Windows 専用機能の扱い | DI: Windows-only features are optional dependencies |
| DNS プリセット、DoH / DoT | DNS Preset Catalog / DoH toggle / DoT toggle |
| ワンクリック最適化 | One-click optimization |
| ゲーム向け NIC 設定 | Gaming NIC profile |
| 現在値の読み取り（ロケール非依存） | Reading current state is locale-independent |
| ネットワーク診断 | Used-PC network diagnostics / Network diagnostics |
| アダプタ一覧・詳細・ゴースト削除 | Adapter list filtering / Adapter details / Disconnected network device cleanup |
| 画面、テーマ、コントロール | UI Framework |
| 設定の保存、ログ | Settings & Logging |

## Key Conventions

- **Async**: service methods use `Async` suffix, propagate `CancellationToken`.
- **Command builders are pure functions**: no `Process`/OS calls inside `*CommandBuilder` / `*Catalog` classes —
  keeps them unit-testable. Only the `*Service` classes (annotated `[SupportedOSPlatform(...)]`) touch the OS.
- **Native AOT and JSON**: keep application-owned JSON on source-generated `System.Text.Json` contexts and verify
  release-path changes with a win-x64 Native AOT publish, not only a framework-dependent build.
- **macOS paths are implemented but unverified on real hardware**: this repo was built entirely on a Windows
  machine. The Windows command catalog was empirically tested (unit tests + a couple of live `netsh`/PowerShell
  invocations during development); the macOS `networksetup`/`osascript`/`ifconfig`/`ping`/`traceroute` code
  compiles, is unit-tested at the parser level, and is written carefully against known command syntax, but has
  not been run end-to-end on an actual Mac. Verify before shipping a macOS build.

## Auto-update & Release (Velopack + Cloudflare R2, Windows-only signed distribution)

Shisui ships as a **signed Velopack app distributed from Cloudflare R2** (Windows only; macOS distribution would
need separate Apple notarization and is not set up).

- **Update source**: `SimpleWebSource` pointing at **`https://shisui.kagayoi.com`** (R2 bucket `shisui-updates`,
  custom domain on the `kagayoi.com` Cloudflare zone). The base URL is hardcoded in `AppSettings.UpdateBaseUrl`
  with `[JsonIgnore]` (not overridable from settings.json — closes the third-party-host redirection attack surface).
  Channel is `win` only (`releases.win.json`).
- **Client wiring**: `Program.cs` calls `VelopackApp.Build().Run()` first (before the single-instance guard).
  Its `OnAfterUpdateFastCallback` moves the legacy `StartMenu\\ゆろち\\Shisui.lnk` shortcut made through
  v1.0.7 into `StartMenuRoot` before Velopack recalculates shortcuts; normal startup retries the same idempotent
  migration if the hook encountered a transient file lock. The migrator never overwrites an existing root shortcut
  and only removes the legacy folder when it is empty. Both the move and the PerUser cleanup send a recursive
  `SHChangeNotify(SHCNE_UPDATEDIR)` notification for the user's Programs folder so Windows Start does not retain
  the removed legacy shortcut as a duplicate cached entry.
  A legacy PerUser install is then migrated to the signed PerMachine MSI before runtime elevation; cancellation
  exits instead of continuing to run the user-writable build as administrator. The MSI normally installs under
  Program Files, but also supports an administrator-selected machine location; migration completion therefore
  accepts an out-of-Program-Files executable only when its HKLM install registration, `.msi-installed` marker,
  and Authenticode publisher all verify. If a user installs the MSI directly while the old PerUser tree still
  exists, the trusted PerMachine startup reconstructs the missing cleanup marker for the exact
  `%LocalAppData%\Shisui` root and performs the same cleanup/retry flow. User settings/logs stay in
  `%APPDATA%\Shisui` and survive the migration.
  Velopack 1.2.0 currently emits a PerMachine MSI whose `INSTALLFOLDER` is directly under `TARGETDIR`, which Windows
  resolves as `C:\Shisui` despite the documented Program Files behavior. `release-local.ps1` therefore runs
  `set-msi-program-files-location.ps1` after `vpk pack`, rewrites the MSI Directory table to
  `ProgramFiles64Folder\Shisui`, and re-signs the modified MSI before signature verification/upload. The in-app
  migration also passes `VELOPACK_INSTALLDIR=<Program Files>\Shisui` to `msiexec` as a defense-in-depth override.
  Clients already installed from a malformed MSI detect only the known `C:\Shisui` and
  `Program Files\ゆろち\Shisui` roots, obtain consent, then use the same signed MSI to repair into Program Files;
  an HKLM-protected marker and HKCU RunOnce preserve cleanup intent if Restart Manager terminates the old process.
  The check/download/apply **UI is the `VelopackUpdateDialog.Avalonia` package** (`UpdateDialogWindow.ShowAsync`),
  not hand-rolled — `UI/Services/UpdateService.cs` only builds the `UpdateManager(SimpleWebSource)` and exposes
  `TryCreateInstalledManager()` (returns null on dev/uninstalled builds). `VersionViewModel.ShowUpdateDialogAsync`
  owns the dialog: `Strings = ShisuiUpdateStrings.Instance` (JP-only, no locale switching unlike Lhamiel),
  `IgnoredTagName`/`VersionIgnored` persist "skip this version" to `AppSettings.IgnoreUpdateTag`, `AccentBrush`
  matches the app's `#0A84FF`. Manual check (更新を確認 button) uses `manualCheck: true` (shows even when
  up-to-date); startup auto-check (gated by `AppSettings.CheckForUpdatesOnStartup`) uses `manualCheck: false` and
  is **deferred via `Dispatcher.UIThread.Post(Background)`** because `Version.Initialize()` runs inside the
  `MainWindowViewModel` ctor — i.e. *before* `desktop.MainWindow` is assigned, so the owner window isn't ready yet.
  In dev (`dotnet run`), `UpdateManager.IsInstalled` is false so `TryCreateInstalledManager` returns null and the
  dialog is skipped — that is expected. Shisui is Native AOT; app-owned JSON serialization uses
  `ShisuiJsonContext`, and update/package changes must pass the win-x64 publish in `release-local.ps1 -SkipUpload`.
  Velopack is referenced directly (not via the dialog package's transitive ref) since `Program.cs`/`UpdateService`
  use it. The Velopack package and `vpk` CLI both pin 1.2.0; `VelopackUpdateDialog.Avalonia` is pinned separately.
  The `vpk` CLI in `release-local.ps1` is **not resolved-latest**: `set-msi-program-files-location.ps1` rewrites
  the MSI Directory table against 1.2.0's layout, so a silently newer vpk could break the rewrite — bump the
  `Velopack` package and `$VpkVersion` together, verifying with `-SkipUpload` first. The dialog package can be
  updated independently, but the same Native AOT release-path verification is required.
- **Release is local + signed, not CI**: `scripts/release-local.ps1` (adapted from `C:\Users\IMT\dev\VStoVSC`)
  does publish (self-contained win-x64) → `vpk pack --msi --instLocation PerMachine` + **Authenticode sign** (Certum "Open Source Code Signing in
  the cloud", `signtool /n "Open Source Developer Yuichiro Shinozaki"`) → signature verify → R2 upload (wrangler) →
  Cloudflare cache purge → manifest-match distribution check → old-version cleanup. R2 publication first backs up
  the currently served metadata, then uploads versioned `.nupkg` payloads, fixed-name binaries, and update metadata
  in that order. A failure after metadata publication restores the backed-up metadata and purges it; cleanup retains
  every manifest-referenced object and the versioned artifacts for the latest two versions so rollback remains
  possible.
  The fixed `Shisui-win.msi` is uploaded before the update manifest and checked for matching served size. The
  generated PerUser `Shisui-win-Setup.exe` is excluded from upload, and its old R2 object/cache entry is removed
  only after MSI propagation succeeds.
  Signing needs
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

## ドメイン移行（2026-07 開始・期限 2027/05/31）

屋号を **Kagayoi** に統一したため、配信ドメインを `nephilim.jp` から `kagayoi.com` へ移行中。方針の全体像はユーザーグローバルの `AGENTS.md` §事業固有の不可逆ガード を参照する。

- **旧ドメイン `nephilim.jp` はレジストラで廃止申請済みで 2027/05/31 に失効する**（延長しない）。それまでに出荷済みバイナリを新ドメインへ移行しきる。
- 旧ホストの Worker route / custom domain は**期限まで消さない**。消すと出荷済みアプリの自動更新が止まる。
- `nephilim.jp` の Redirect Rules は `/` だけを 301 する。`releases.*.json` / `*.nupkg` / `*-Setup.exe` は転送せず R2 が配信を続ける。
- 配信は `shisui.kagayoi.com`（R2 `shisui-updates`）。旧 `shisui.nephilim.jp` は route に併記して残してある。
