# AGENTS.md

This file provides guidance to Codex (ChatGPT) and other coding agents working in this repository.

## Project Overview

Shisui is a cross-platform (Windows / macOS) desktop app for network configuration. It lets the user switch
DNS providers (Cloudflare standard / malware-block / malware+adult-block, Google Public DNS, Quad9, NextDNS, or
custom IPv4/IPv6), flush the DNS cache, run a Ping/Traceroute diagnostics tool, and — on Windows only — toggle
DNS over HTTPS (DoH) and DNS over TLS (DoT) for the selected preset, toggle BBR2 congestion control / TCP global
options (including receive-window auto-tuning and per-adapter MTU restoration to 1500), run a catalog of `netsh` / `ipconfig` /
`nbtstat` network maintenance commands, view read-only adapter details (MAC address / link speed), and clean up
disconnected "ghost" network devices.

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
Debug builds still perform runtime elevation because TCP/DNS benchmarks mutate system-wide settings. To keep a
debugger attached while exercising those paths, start Visual Studio itself as administrator; a non-elevated IDE
causes the first process to relaunch elevated and detach from that original debugging session.

Windows releases are installed by the signed Velopack **PerMachine MSI** under protected `Program Files`; do not
publish the generated PerUser `Setup.exe`. Legacy `%LocalAppData%\Shisui` builds are the one-time exception:
`WindowsPerMachineMigration` runs before whole-app elevation, downloads `Shisui-win.msi` from the fixed R2 origin,
validates it with `WinVerifyTrust` plus the expected publisher CN, and invokes system `msiexec` with `runas`.
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

### Project Structure

- **Shisui.Core** — Interfaces, models, and all OS-interacting services. No UI dependency.
  - `Interfaces/` — `INetworkAdapterService`, `IDnsConfigurationService`, `IDohConfigurationService`,
    `IDotConfigurationService`, `IDnsCacheService`, `ITcpTuningService`, `INetworkMaintenanceService`,
    `IGhostAdapterService`, `INetworkAdapterNameService`, `INetworkDiagnosticsService`,
    `IAutoTuningBenchmarkService`, `IRscBenchmarkService`,
    `IBbr2BenchmarkService`, `ITcpOptionBenchmarkService`,
    `ILegacyNetworkDiagnosticsService`,
    `ILoadedPingMeasurementService`, `ICommandExecutor`,
    `ISettingsService`.
  - `Models/` — `NetworkAdapterInfo`, `NetworkAdapterDetails` (MAC/link speed, read-only), `DnsServerSet`,
    `DnsProviderPreset` (has nullable `DohTemplate` / `DotHost`), `DnsPresetCatalog` (hardcoded official
    Cloudflare/Google/Quad9/NextDNS IPs + DoH/DoT hostnames), `DohStatus`, `TcpSettingsSnapshot` (includes
    `AutoTuningLevel`), `PingResult`, `TraceRouteResult`/`TraceRouteHop`, `GhostAdapterInfo`,
    `MaintenanceCommandDefinition`, `CommandExecutionResult`, `AppSettings`.
  - `Services/Windows/` — netsh/ipconfig/PowerShell-backed implementations. Command *building* (pure string
    formatting) is split from command *execution* (`ICommandExecutor`) so the exact command strings are
    unit-testable without touching the OS. See `WindowsDnsCommandBuilder`, `WindowsTcpCommandBuilder`,
    `WindowsMaintenanceCommandCatalog`, and the `*CommandBuilder`/`*Parser` pairs for DoT/ping/traceroute/MTU/
    adapter-details covered in the sections below.
  - `Services/MacOS/` — `networksetup`/`dscacheutil`/`ifconfig`-backed implementations, plus
    `MacElevatedCommandExecutor` which wraps every command through `osascript -e 'do shell script "..." with
    administrator privileges'` (macOS apps should not request a blanket admin launch the way Windows apps do via
    manifest).
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

**Output decoding is auto-detected, not a fixed encoding**: `ProcessCommandExecutor` reads stdout/stderr as **raw
bytes** (both `BaseStream`s copied concurrently to avoid pipe deadlock), then decodes with a heuristic — strict
UTF-8 first, falling back to the OEM code page (`CultureInfo.CurrentCulture.TextInfo.OEMCodePage`, = CP932 on
Japanese Windows) on `DecoderFallbackException`. This is deliberate and empirically necessary: `netsh`/`ipconfig`
emit **UTF-8 on some machines and OEM/CP932 on others** (depends on the console output code page inheritance,
which for a GUI app with no console resolves unpredictably), so a fixed `StandardOutputEncoding` mojibakes one
environment or the other. Verified on this machine (a WinExe/no-console harness through the real executor):
netsh emitted UTF-8 while the GUI's `Console.OutputEncoding` was CP932, which is exactly the mojibake case the
old fixed-decode path produced (「繧｢繧ｯ繝・…」). CP932 Japanese byte sequences are essentially never valid
strict UTF-8, so the try-UTF-8-then-OEM order self-detects safely. `CodePagesEncodingProvider` is registered in
`ProcessCommandExecutor`'s static ctor (needed for `GetEncoding(932)`); it ships in the .NET 10 shared framework,
so **no `System.Text.Encoding.CodePages` PackageReference is needed** (adding it triggers an NU1510 prune
warning). `DecodeConsoleOutput` is `internal` + unit-tested (`ProcessCommandExecutorDecodeTests`).

### DI: Windows-only features are optional dependencies, not stubbed

`ITcpTuningService`, `INetworkMaintenanceService`, `IGhostAdapterService`, `INetworkAdapterNameService`, and
`IDohConfigurationService` are
only registered in `App.axaml.cs` when `OperatingSystem.IsWindows()`. Consumers take them as
constructor parameters with explicit `= null` defaults — `Microsoft.Extensions.DependencyInjection` only
substitutes `null` for an unregistered service type when the constructor parameter has a default value; without
`= null` it throws `InvalidOperationException` at startup on macOS. This applies to `MainWindowViewModel`
(`TcpTuningViewModel? = null`, `MaintenanceViewModel? = null`; the corresponding tabs in `MainWindow.axaml` are
gated on `IsVisible="{Binding IsWindows}"`) and to `DnsSettingsViewModel` (`INetworkAdapterNameService? = null`,
`IDohConfigurationService? = null`; the connection-name cleanup card and DoH checkbox are gated on
`IsAdapterNameCleanupAvailable` / `IsDohAvailable`), never on null-checking directly in bindings.

### DNS Preset Catalog (`Core/Models/DnsPresetCatalog.cs`)

Hardcoded official addresses + DoH/DoT hostnames — do not "correct" these without checking the provider's current
documentation. **The DoH/DoT hostname differs per Cloudflare filtering tier** (`security.` / `family.`
subdomains, verified against developers.cloudflare.com): using the plain `cloudflare-dns.com` hostname for the
malware / malware+adult tiers would silently give *unfiltered* encrypted resolution — a functional bug that
defeats the preset. Google and Quad9 each use one hostname for all their IPs; **NextDNS has neither `DohTemplate`
nor `DotHost`** (both left `null`) because its encrypted-DNS endpoints are per-account subdomains that can't be
expressed as a fixed value — the plain IPs are "Linked IP" addresses that only apply filtering once the user
links their current IP in the NextDNS dashboard; the custom preset likewise has no DoH/DoT hostname (both hidden
in the UI for it).

| Preset | IPv4 | IPv6 | DoH template | DoT host |
|---|---|---|---|---|
| Cloudflare 標準 | 1.1.1.1 / 1.0.0.1 | 2606:4700:4700::1111 / ::1001 | `https://cloudflare-dns.com/dns-query` | `cloudflare-dns.com` |
| Cloudflare マルウェアブロック | 1.1.1.2 / 1.0.0.2 | 2606:4700:4700::1112 / ::1002 | `https://security.cloudflare-dns.com/dns-query` | `security.cloudflare-dns.com` |
| Cloudflare マルウェア+アダルトブロック | 1.1.1.3 / 1.0.0.3 | 2606:4700:4700::1113 / ::1003 | `https://family.cloudflare-dns.com/dns-query` | `family.cloudflare-dns.com` |
| Google Public DNS | 8.8.8.8 / 8.8.4.4 | 2001:4860:4860::8888 / ::8844 | `https://dns.google/dns-query` | `dns.google` |
| Quad9 | 9.9.9.9 / 149.112.112.112 | 2620:fe::fe / ::9 | `https://dns.quad9.net/dns-query` | `dns.quad9.net` |
| NextDNS (Linked IP) | 45.90.28.0 / 45.90.30.0 | 2a07:a8c0:: / 2a07:a8c1:: | — | — |

### DNS over HTTPS (DoH) toggle (`IDohConfigurationService`, Windows)

The DNS tab shows a DoH checkbox for presets that have a `DohTemplate` (all built-ins except カスタム). Enabling
registers each of the preset's IPs via `netsh dnsclient add encryption server=<ip> dohtemplate=<url>
autoupgrade=yes udpfallback=yes` and then `netsh dnsclient set global doh=yes`; disabling runs `netsh dnsclient
delete encryption server=<ip> protocol=doh` per IP (global `doh` is left alone — it can affect other
registrations). See the next section for the DoT sibling toggle, which uses the same `netsh dnsclient` family
but is architecturally different because it has no state-read cmdlet.

**The checkbox reflects real OS state, it is not a saved preference.** `WindowsDohStateCommandBuilder` /
`WindowsDohStateParser` (pure, unit-tested, same locale-independent `KEY=VALUE` PowerShell pattern as the TCP tab)
read `Get-DnsClientDohServerAddress -ServerAddress <ips>` and classify `DohStatus` = Enabled (all IPs
`AutoUpgrade=True`) / Partial / Disabled / Unknown. `DnsSettingsViewModel.RefreshDohStateAsync` runs on startup
and on preset change, setting `UseDoh = (status == Enabled)`. The *selected preset* is persisted
(`AppSettings.LastSelectedPresetId`, restored via direct field assignment to avoid firing `OnSelectedPresetChanged`)
so that the DoH state is read against the preset the user last used, not always the default.

### DNS over TLS (DoT) toggle (`IDotConfigurationService`, Windows)

The DoT checkbox sits next to DoH's for presets that have a `DotHost`, driving the same
`netsh dnsclient add/delete encryption ... dothost=<host>` shape as DoH's `dohtemplate=`. Unlike
`IDohConfigurationService`, **it has no `GetStatusAsync` and is deliberately state-less**: no
`Get-DnsClientDotServerAddress` cmdlet exists (only DoH got one), and `netsh dnsclient show encryption`'s text
can't fill the gap either — its labels are Japanese-localized on this machine (e.g. 「DNS-over-TLS ホスト」), the
exact locale trap this project's "PowerShell English-fixed properties only" rule exists to avoid. So the checkbox
is **fire-and-forget**: always unchecked on load/preset-change, just fires enable/disable on click, unlike DoH's
checkbox which reflects real OS state.

Enabling DoH and DoT on the same IP simultaneously is safe — confirmed empirically (2026-07, real machine): the
two `add encryption` calls merge into independent per-protocol blocks instead of one overwriting the other, and a
`pktmon` capture showed Windows using **both** at once (no exclusive priority, zero plaintext/port-53 fallback).
Their connection shapes differ, though: DoH's connections stay open and get reused, DoT reconnects (fresh TLS
handshake) roughly every 1.5–1.8s — and that asymmetry showed up in a `Resolve-DnsName` latency benchmark on this
machine as DoH being slightly faster and more consistent (~53ms avg) than DoT (~58ms avg), contrary to the common
"DoT is lighter/faster" claim. Full methodology/caveats (single machine, single run — not a general benchmark) are
in the XML doc on `IDotConfigurationService`.

### One-click optimization (`RunOneClickOptimizationAsync`, `DnsSettingsViewModel`)

The 「自動最適化」 tab's 「クイック最適化」 card exposes the DNS tab's shared adapter selection and a
「おまかせ高速化設定」 button for users who don't want to understand each individual toggle. The tab-level
`AutoOptimizationViewModel` command delegates the mutation to `DnsSettingsViewModel.RunOneClickOptimizationAsync`
and then refreshes the manual TCP tab's state badges. On click it: switches the selected preset to
Cloudflare standard, enables DoH if the preset supports it, flushes the DNS cache, and — when
`INetworkMaintenanceService` is available (Windows; injected as `INetworkMaintenanceService? = null` the same
way the other Windows-only services are, so macOS silently skips this step) — also runs only maintenance commands
whose `MaintenanceCommandDefinition.IncludeInOneClickOptimization` flag is true: NetBIOS name-cache purge/reload,
all-interface IPv4 ARP-cache flush (`netsh interface ipv4 delete arpcache`), IPv4/IPv6 destination-cache flushes,
the IPv6 neighbor-discovery cache flush, and `netsh winsock set autotuning on` so old tweak tools cannot leave
Winsock's independent send-buffer autotuning disabled. This is an explicit allowlist: DNS/NetBIOS registration and HTTP.sys
log-buffer/server-response-cache operations remain available in the maintenance tab but are deliberately excluded
from one-click because they do not optimize ordinary client or game traffic. DNS cache flushing is already handled
once through `IDnsCacheService`. Finally
— only when `ITcpTuningService` is available (Windows, same optional-injection pattern) — restores all five TCP
templates and any other user-configured TCP parameters with the official `netsh int tcp reset`, then deliberately
runs explicit fallback resets for congestion providers and common tweak-tool targets (RSS, RSC, ECN, timestamps,
initial RTO, non-SACK resiliency, SYN retries, Fast Open/fallback, HyStart, PRR, pacing, and force-window-scaling),
removes only the per-interface legacy tweak values `TcpAckFrequency`, `TCPNoDelay`, and `TcpDelAckTicks` from
`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\<GUID>` so Windows falls back to its defaults,
enables IPv4/IPv6 loopback Large MTU, and resets receive-window auto-tuning to Normal. The registry command emits
`REMOVED=N`, never deletes an interface key, and deliberately does not touch the unrelated MSMQ `TCPNoDelay` value.
Because Microsoft documents these delayed-ACK registry changes as requiring a restart, the one-click description
and success status tell Windows users to restart the PC. The explicit netsh commands make
partial failures visible in the execution log and recover supported settings even if the aggregate reset fails.
The TCP tab exposes the aggregate TCP reset, explicit global-option reset, and legacy ACK/Nagle registry cleanup,
while the maintenance tab exposes Winsock send autotuning as a separate command,
as three separate commands as well; one-click must not be the only UI path to any setting mutation it performs.
This normalization intentionally stops at documented TCP/netsh state and those three specifically named legacy
per-interface values: it does not delete arbitrary registry values,
change NIC driver advanced properties, alter BCD, or replace power plans. DoT is deliberately left untouched (see the DoT section above: DoH
measured slightly faster and more consistent, so there's little benefit to enabling both). On Windows, one-click
finishes by calling `INetworkAdapterNameService.CleanupAsync` with the selected connection name: it removes every
disconnected PnP network-device registration and, when possible, strips the selected live adapter's numeric suffix.
This must run after every operation that uses the old connection name; if a rename succeeds, the new name is persisted
before adapters are reloaded. Disabled live devices are preserved, but unplugged USB LAN and dock NIC registrations
are intentionally included and the button description warns that Windows will redetect them when reconnected. Other
destructive maintenance actions (per-adapter MTU restoration and the 「ファイアウォール・スタックリセット」category)
remain excluded. Since the congestion-provider reset / TCP global-option reset / loopback Large MTU / auto-tuning /
cache-maintenance commands are global, not scoped
to the selected adapter (unlike the DNS change), the button's description text calls this out explicitly for
multi-NIC environments.

Because `SelectedPreset`'s setter would trigger `OnSelectedPresetChanged`'s fire-and-forget
`RefreshDohStateAsync` call (racing against this method's own `await`ed call at the end), the preset switch here
assigns the backing field directly and raises the needed `OnPropertyChanged` notifications by hand instead of
going through the property setter (2026-07-06, found via `/rere` review — the two `RefreshDohStateAsync` calls
could complete out of order and leave the DoH checkbox showing a stale state).

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

**Auto-tuning level** rides in the same one-line script (an `AUTOTUNE=` token added to the existing
`Get-NetTCPSetting` call, parsed from `AutoTuningLevelLocal`), so reading it costs no extra process spawn. **MTU
is different**: it's per-adapter rather than global, so `WindowsMtuStateCommandBuilder`/`WindowsMtuStateParser`
are a separate one-line `Get-NetIPInterface -InterfaceAlias <adapter>` command taking an adapter name, invoked
whenever the MTU restoration card's own adapter selection changes rather than folded into the global TCP snapshot.
That card exposes no arbitrary MTU or jumbo-frame input: it only restores both IPv4 and IPv6 MTU to the standard
1500 value. It also keeps **its own adapter selector** in `TcpTuningViewModel`, independent of the DNS tab's — the
two tabs' adapter choices are unrelated (MTU restoration targets one adapter; BBR2/global TCP options apply system-wide).

### Unified measured optimization (`AutoOptimizationViewModel`, Windows)

All A/B measurement UI lives in the left-sidebar 「自動最適化」 tab, not in the manual BBR2/TCP tab. The single
「すべて計測」 command runs Auto-Tuning, BBR2, RSC, ECN, RSS, and TCP Timestamps in that order and builds one
recommended configuration. Auto-Tuning and RSS use download Mbps; the other four binary settings use loaded Ping.
The fixed run is 15 Auto-Tuning samples plus 6 samples for each of five binary settings: 45 total, up to about
225MB at 5MB per sample. Each benchmark restores its starting state before the next begins. Recommendations are
only suggestions until the user presses
「推奨設定を一括適用」; that command applies whichever recommendations were successfully determined under one
`INetworkMutationGate` lease and logs every resulting command. Loaded-Ping differences below 1ms and RSS speed
differences below 3% are treated as inconclusive and recommend the actual Windows default instead of leaving the
item unapplied. The fallback is Auto-Tuning Normal, default congestion providers for BBR2, `default` for
RSC/ECN/RSS, and the documented default `Allowed` for TCP Timestamps. The separate 「クイック最適化」
flow is not automatically chained into measurement because its legacy ACK changes require a PC restart; the UI
instructs users to quick-optimize, restart, and then measure.
The ViewModel rejects a benchmark before mutation when the process is not elevated and writes every benchmark
exception to the normal Shisui file log; do not silently reduce an authorization failure to an empty result set.

### Auto-tuning benchmark (`IAutoTuningBenchmarkService`, Windows)

Lets the user measure, rather than guess, which auto-tuning level suits their connection: it cycles through all
5 levels and reports **TCP download throughput only**. Auto-tuning affects TCP receive-window scaling rather than
ICMP, so Ping is not measured or used to choose the best level. Each sample downloads 5MB from a Hetzner
test-file host and calculates Mbps from the received bytes and elapsed body-read time.

The speed measurement itself lives in `WindowsDownloadSpeedMeasurementService`; the RSC A/B benchmark continues
to use `WindowsLoadedPingMeasurementService` because RSC compares latency under TCP receive load. Both measurement
services use the shared `WindowsBenchmarkDownloadCatalog` so their Hetzner target sequence, range requests, size
limit, and HTTP error formatting remain aligned. Each Auto-Tuning level gets a fresh `HttpClient` and reports
average/min/max Mbps.

Two correctness points worth knowing before touching this code: (1) **a fresh `HttpClient` per level is
required**, not just per run — the window scale is negotiated once at the TCP handshake and does not change for
a connection's lifetime, so reusing a client across a level switch would silently measure the *previous* level's
window; and (2) the original level must parse as a known `AutoTuningLevel` before any change is allowed. Each
level switch checks its `CommandExecutionResult`; a failed switch is reported as a failed row and is not measured.
The original level is restored with `CancellationToken.None` in a `finally` around the whole loop, and restore
failure is surfaced as an error rather than success. `AutoOptimizationViewModel` retains the best result as a
recommendation; the benchmark itself never leaves that level applied.

**Each level is always sampled 3 times and averaged, not measured once.** The sample count is deliberately fixed:
it retains multi-sample averaging while limiting traffic and runtime. `RunAsync` switches
each level, then `WindowsDownloadSpeedMeasurementService.MeasureAsync` loops samples with a 150ms inter-sample
delay, streams exactly the requested body size, and calculates decimal Mbps. The pure, tested benchmark math turns
successful speed samples into average/min/max; a level only reports `Success = false` if every sample failed.
`AutoTuningBenchmarkResult` carries `AverageMbps`/`MinMbps`/`MaxMbps`/`SampleCount`, and the UI marks the
**highest average speed** as best. `AutoTuningBenchmarkProgress`'s
`CompletedCount`/`TotalCount` count individual samples across *all* levels (e.g. `8/15` for 5 levels × 3
samples), not per-level.

`WindowsBenchmarkDownloadCatalog.Targets` lists five Hetzner regions (fsn1/nbg1/hil/sin/ash). The rotation index is the sample index within each
level, so every level sees the same target sequence and a regional load-source difference does not privilege one
level. The load size is fixed by `AutoOptimizationViewModel.BenchmarkLoadSizeBytes = 5_000_000`, and the shared sample count is
fixed at 3 in `WindowsBenchmarkDownloadCatalog`; neither is user-configurable. A non-2xx response or incomplete
download is excluded from that level's average.

### RSC low-latency A/B benchmark (`IRscBenchmarkService`, Windows)

The measured-optimization card compares RSC enabled versus disabled with loaded Ping. It measures each state a
fixed 3 times (not user-configurable), using the same 5MB load size and
the same Hetzner target sequence for both states. This tests whether TCP receive coalescing changes latency while a
TCP receive is active; it does not claim to measure OW2's UDP game packets directly. A difference below 1ms is
treated as inconclusive and the UI recommends restoring the Windows default state.
Keep RSC's Ping target identical across every sample when changing the shared download targets.

`WindowsRscBenchmarkService` reads the effective RSC state before changing anything, refuses to run if that state
cannot be read safely, and restores it with `CancellationToken.None` in a `finally` on completion, cancellation, or
exception. A failed restore is surfaced as an error rather than reported as a successful benchmark.

### Additional TCP A/B benchmarks (`IBbr2BenchmarkService` / `ITcpOptionBenchmarkService`, Windows)

`WindowsBbr2BenchmarkService` compares BBR2 against the default congestion provider without touching loopback
Large MTU. `WindowsTcpStateCommandBuilder` emits each of the five template names with its provider, so the service
can restore a mixed/custom starting configuration exactly in `finally`; it refuses to run if any template is
missing. Permanent application still uses the existing BBR2 enable/revert profile.

`WindowsTcpOptionBenchmarkService` compares ECN and TCP Timestamps with loaded Ping and RSS with download speed.
Each state is fixed at 3 samples. It accepts only an exact `Enabled` or `Disabled` starting value and restores that
value with `CancellationToken.None`. TCP Timestamps also accepts the documented Windows default `Allowed`; after
the Enabled/Disabled comparison it restores that third state through `RevertTcpGlobalOptionToDefaultAsync`, which
emits `timestamps=allowed`. Other unrecognized values are rejected before mutation. Fast Open remains manual because its current state is not
available through the locale-independent state reader. MTU is not automatically probed or tuned because doing so
can break connectivity; the manual UI only restores a selected adapter to 1500.

### Used-PC network diagnostics (`ILegacyNetworkDiagnosticsService`, Windows)

The 「自動最適化」 tab has a read-only 「使い込んだPC向けネットワーク診断」 card. For the selected adapter,
`WindowsLegacyNetworkDiagnosticsService` reads `Get-NetAdapterStatistics` and `Get-NetAdapter` through fixed
`KEY=VALUE` PowerShell output, checks the global `DisableTaskOffload` value without changing it, reads Winsock send
autotuning, counts `pnputil /enum-devices /problem /class Net /format xml` results, and reuses
`IGhostAdapterService` for disconnected network devices. Packet errors always warn; discards warn only when at
least 100 and at least 0.1% of observed packets, avoiding noisy single discards. A driver date over five years old
is a check recommendation, not proof that the driver is wrong.

Findings guide the user to the DNS-tab connection-name cleanup action and maintenance-tab Winsock reset. NIC advanced
property reset is offered only when the report recommends it and targets only the currently selected adapter via
`Reset-NetAdapterAdvancedProperty -DisplayName '*'`; it is destructive, restarts the adapter, and is never part of
one-click optimization. IP-stack reset and `netcfg -d` remain last-resort maintenance actions and are not run by
the diagnostic flow.

All mutating DNS/TCP/maintenance paths share the singleton `INetworkMutationGate` / `NetworkMutationGate`, including
both benchmarks, one-click optimization, manual TCP changes, DNS apply/reset/cache flush, MTU changes, connection-name
cleanup (including disconnected-device removal), and maintenance batches. Benchmark services hold the lease from the initial state read through the final
restore; ViewModels must not acquire the same gate around a benchmark call because the gate is intentionally
non-reentrant. ViewModel busy flags remain local UI affordances, while the shared gate is the cross-ViewModel
correctness boundary.

### Network diagnostics (`INetworkDiagnosticsService`, cross-platform)

Same locale trap as the TCP-state badges, different tool: `ping.exe`/`tracert.exe` text output is localized, so
neither Windows parser touches it directly. `WindowsPingCommandBuilder` uses `Test-Connection` (`StatusCode`/
`ResponseTime`, English-fixed numeric properties) instead of `ping.exe`; `WindowsTraceRouteCommandBuilder` gets
the hop path from `Test-NetConnection -TraceRoute`'s `.TraceRoute` property (a plain ordered IP-address array,
not text) and then, since that cmdlet doesn't also report per-hop RTT, issues one follow-up
`WindowsPingCommandBuilder` ping per discovered hop to time it (a Service-layer responsibility, not the pure
builder's). macOS's `MacPingResultParser`/`MacTraceRouteParser` parse `ping`/`traceroute` output directly instead
— BSD ping/traceroute's own text (`X packets transmitted, Y packets received`, `round-trip min/avg/max/stddev`)
is a fixed English format regardless of macOS's system language, so the locale trap doesn't apply there.
`INetworkDiagnosticsService` is one abstraction shared by two call sites: the DNS tab's 疎通テスト button (pings
whichever DNS IP is currently selected) and the standalone ネットワーク診断 tab. The standalone tab offers
`NetworkDiagnosticTargetCatalog` presets (local loopback, three public DNS targets, Google, and GitHub); selecting
one copies its host into the same field used by Ping and traceroute. Free-form host/IP input remains available,
and editing it clears the preset selection so the UI never implies that a custom value is still the selected preset.

### Adapter list filtering (`WindowsNetworkAdapterFilter`, pure)

`NetworkInterface.GetAllNetworkInterfaces()` returns real adapters plus a lot of noise: NDIS filter/binding
**child interfaces** (`<parent>-QoS Packet Scheduler-0000`, `<parent>-WFP ...Filter-0000`), Wi-Fi Direct virtual
adapters, and `NotPresent` pseudo-devices. The DNS dropdown must match what `ncpa.cpl` shows. The pure filter keeps
Ethernet/Wireless80211 that are Present, drops a "binding child" if its Description is `<another adapter's
Description>-...` (structural — works for unknown antivirus/VPN LWFs without a hardcoded name list), and drops
`Wi-Fi Direct Virtual Adapter` and `WAN Miniport*` by their (locale-stable, English) driver descriptions — most
WAN Miniports report as Ppp and die at the type check, but WAN Miniport (IP)/(IPv6)/(Network Monitor) report as
Ethernet+Up and leaked into the dropdown as 「ローカル エリア接続* 6〜8」 (real machine, 2026-07-08). VPN TAP /
Hyper-V vEthernet stay.

### Adapter details (`NetworkAdapterDetails`, read-only, both platforms)

Windows reads MAC address / link speed / media type / status in one call: `WindowsAdapterDetailsCommandBuilder`
runs `Get-NetAdapter -Name <adapter>` and emits `KEY=VALUE` lines (same locale-independent pattern as the TCP/DoH
state readers), parsed by `WindowsAdapterDetailsParser`. **macOS needs two calls, not one**, because
`networksetup`'s "network service" names (what `-listallnetworkservices` and the DNS dropdown use) aren't the BSD
device names `ifconfig` expects: `MacNetworkAdapterService.GetAdapterDetailsAsync` first runs
`networksetup -listnetworkserviceorder` and parses it (`MacNetworkServiceOrderParser`) into a service-name→device
(e.g. `en0`) map, then runs `ifconfig <device>` and parses *that* (`MacIfConfigParser`) for the actual details —
a service name alone can't be `ifconfig`'d directly.

### Disconnected network device cleanup backend (`IGhostAdapterService`, Windows)

`IGhostAdapterService` enumerates **disconnected** network-class devices via
`pnputil /enum-devices /disconnected /class Net /format xml` (XML for locale-independent parsing) and removes an
instance via `pnputil /remove-device "<InstanceId>"`. It is a shared backend, not an independent per-device UI:
`WindowsNetworkAdapterNameService` removes every returned registration as the first phase of the DNS-tab
「接続名の連番を整理」 action, while `WindowsLegacyNetworkDiagnosticsService` reuses the same enumeration for its
read-only count. This uses pnputil's proper PnP removal path rather than raw registry edits.

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
- Main view: `MainWindow` (880×620, min 760×500) is a sidebar `TabControl` — DNS 設定 / 自動最適化 /
  ネットワーク診断 / BBR2・TCP 調整 / メンテナンス (the TCP and maintenance tabs use
  `IsVisible="{Binding IsWindows}"`) / バージョン — plus a persistent
  実行ログ card fed by each tab ViewModel's `CommandExecuted` event, one `UserControl` per tab. Sidebar tab icons
  are hand-drawn with plain Avalonia primitives (`Ellipse`/`Line`/`Path`/`Polyline`/`Rectangle`+`RotateTransform`)
  bound to `{DynamicResource Brush.FG1}` — no icon font/package, no `PathIcon`. Before committing hand-computed
  coordinates to XAML, preview the shape first (write it as standalone SVG, publish via the `Artifact` tool,
  screenshot it) — a first-draft wrench icon for メンテナンス read as a magnifying glass once rendered and was
  redrawn as an 8-tooth gear before it ever reached `MainWindow.axaml`.
- Destructive maintenance commands are visually flagged (red button via `Classes.danger="{Binding
  Definition.IsDestructive}"`) but run immediately on click — no confirmation checkbox or modal dialog gates them.
- Some maintenance categories also expose a "まとめて実行" (run-all) button that runs every command in the
  category sequentially in catalog order. The batchable set lives in
  `WindowsMaintenanceCommandCatalog.BatchableCategoryLabels` (キャッシュ・登録 / IP アドレス再取得 /
  プロキシ設定リセット) — deliberately **excluding** the destructive スタックリセット category (running all of
  advfirewall/winsock/tcp/ip resets blindly is a footgun) and the single-command component-rediscovery category.
  IP reacquire is the motivating case: `ipconfig /release` alone just drops connectivity, so release→renew is
  only meaningful as a sequence.

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
  machine. The Windows command catalog was empirically tested (unit tests + a couple of live `netsh`/PowerShell
  invocations during development); the macOS `networksetup`/`osascript`/`ifconfig`/`ping`/`traceroute` code
  compiles, is unit-tested at the parser level, and is written carefully against known command syntax, but has
  not been run end-to-end on an actual Mac. Verify before shipping a macOS build.

## Auto-update & Release (Velopack + Cloudflare R2, Windows-only signed distribution)

Shisui ships as a **signed Velopack app distributed from Cloudflare R2** (Windows only; macOS distribution would
need separate Apple notarization and is not set up).

- **Update source**: `SimpleWebSource` pointing at **`https://shisui.nephilim.jp`** (R2 bucket `shisui-updates`,
  custom domain on the `nephilim.jp` Cloudflare zone). The base URL is hardcoded in `AppSettings.UpdateBaseUrl`
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
  dialog is skipped — that is expected. Shisui is **not** AOT/trimmed, so no `TrimmerRootAssembly` entries are
  needed (unlike Lhamiel). Velopack is referenced directly (not via the dialog package's transitive ref) since
  `Program.cs`/`UpdateService` use it; both pin 1.2.0.
- **Release is local + signed, not CI**: `scripts/release-local.ps1` (adapted from `C:\Users\IMT\dev\VStoVSC`)
  does publish (self-contained win-x64) → `vpk pack --msi --instLocation PerMachine` + **Authenticode sign** (Certum "Open Source Code Signing in
  the cloud", `signtool /n "Open Source Developer Yuichiro Shinozaki"`) → signature verify → R2 upload (wrangler) →
  Cloudflare cache purge → manifest-match distribution check → old-nupkg cleanup. R2 publication first backs up
  the currently served metadata, then uploads versioned `.nupkg` payloads, fixed-name binaries, and update metadata
  in that order. A failure after metadata publication restores the backed-up metadata and purges it; cleanup retains
  the `.nupkg` files referenced by both the new and immediately previous manifests so rollback remains possible.
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
