# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Shisui is a cross-platform (Windows / macOS) desktop app for network configuration. It lets the user switch
DNS providers (Cloudflare standard / malware-block / malware+adult-block, Google Public DNS, Quad9, NextDNS, or
custom IPv4/IPv6), flush the DNS cache, run a Ping/Traceroute diagnostics tool, and — on Windows only — toggle
DNS over HTTPS (DoH) and DNS over TLS (DoT) for the selected preset, toggle BBR2 congestion control / TCP global
options (including receive-window auto-tuning and MTU/jumbo-frame size), run a catalog of `netsh` / `ipconfig` /
`nbtstat` network maintenance commands, view read-only adapter details (MAC address / link speed), and clean up
disconnected "ghost" network devices.

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
`VelopackApp.Build().Run()` — `WindowsElevationHelper.IsRunningAsAdministrator()` checks the current token, and
if not elevated, `TryRelaunchElevated` restarts the process via `ShellExecute` + the `runas` verb (one UAC
prompt at startup, matching the original design intent of not re-prompting per command) and exits the
non-elevated instance. This check runs *before* `SingleInstanceGuard` is acquired, so the non-elevated process
never holds the single-instance lock while its elevated replacement starts.

## Architecture

### Project Structure

- **Shisui.Core** — Interfaces, models, and all OS-interacting services. No UI dependency.
  - `Interfaces/` — `INetworkAdapterService`, `IDnsConfigurationService`, `IDohConfigurationService`,
    `IDotConfigurationService`, `IDnsCacheService`, `ITcpTuningService`, `INetworkMaintenanceService`,
    `IGhostAdapterService`, `INetworkDiagnosticsService`, `IAutoTuningBenchmarkService`, `ICommandExecutor`,
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

`ITcpTuningService`, `INetworkMaintenanceService`, `IGhostAdapterService`, and `IDohConfigurationService` are
only registered in `App.axaml.cs` when `OperatingSystem.IsWindows()`. Consumers take them as
constructor parameters with explicit `= null` defaults — `Microsoft.Extensions.DependencyInjection` only
substitutes `null` for an unregistered service type when the constructor parameter has a default value; without
`= null` it throws `InvalidOperationException` at startup on macOS. This applies to `MainWindowViewModel`
(`TcpTuningViewModel? = null`, `MaintenanceViewModel? = null`; the corresponding tabs in `MainWindow.axaml` are
gated on `IsVisible="{Binding IsWindows}"`) and to `DnsSettingsViewModel` (`IGhostAdapterService? = null`,
`IDohConfigurationService? = null`; the ghost-cleanup card and DoH checkbox are gated on `IsWindows` /
`IsDohAvailable`), never on null-checking directly in bindings.

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

### One-click optimization (`OneClickOptimizeAsync`, `DnsSettingsViewModel`)

Next to the adapter selector on the DNS tab, a 「おまかせ高速化設定」(recommended one-click setup) button targets
users who don't want to understand each individual toggle. On click it: switches the selected preset to
Cloudflare standard, enables DoH if the preset supports it, flushes the DNS cache, and — when
`INetworkMaintenanceService` is available (Windows; injected as `INetworkMaintenanceService? = null` the same
way the other Windows-only services are, so macOS silently skips this step) — also runs every command in the
maintenance catalog's 「キャッシュ・登録」category except `ipconfig-flushdns` (already covered by the DNS cache
flush above; excluded by command ID via `DnsFlushCommandId` to avoid running it twice): NetBIOS name cache
purge/re-register, a DNS registration refresh, the HTTP.sys log buffer/response cache, the ARP cache
(`arp-clear` — moved into this category from 「ファイアウォール・スタックリセット」, since it was always a
non-destructive, single-adapter-independent cache clear and never belonged in the destructive-reset bucket),
and the IPv4/IPv6 destination cache (learned next-hop routes) plus the IPv6 neighbor-discovery cache (`arp`'s
IPv6 equivalent). This targets PCs that have accumulated stale cache/route state over a long uptime — the DNS
flush alone doesn't touch any of these. Finally
— only when `ITcpTuningService` is available (Windows, same optional-injection pattern) — enables BBR2 and
resets receive-window auto-tuning to Normal. DoT is deliberately left untouched (see the DoT section above: DoH
measured slightly faster and more consistent, so there's little benefit to enabling both), and destructive
maintenance actions (ghost adapter removal, MTU changes, the 「ファイアウォール・スタックリセット」category) are
intentionally excluded from this button — the one-click flow only touches operations judged safe for a user who
doesn't know what they're doing. Since BBR2 / auto-tuning / the cache-maintenance commands are global, not scoped
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
whenever the MTU card's own adapter selection changes rather than folded into the global TCP snapshot. That MTU
card also keeps **its own adapter selector** in `TcpTuningViewModel`, independent of the DNS tab's — the two
tabs' adapter choices are unrelated (MTU targets one adapter; BBR2/global TCP options apply system-wide).

### Auto-tuning benchmark (`IAutoTuningBenchmarkService`, Windows)

Lets the user measure, rather than guess, which auto-tuning level suits their connection: it cycles through all
5 levels, and for each one downloads a configurable-size payload to measure real throughput. **Ping/latency
can't reveal any difference between levels** — auto-tuning only affects TCP receive window scaling, which ICMP
never touches — so this had to be a real download-throughput measurement, unlike the DoH/DoT latency benchmarks
elsewhere in this doc.

Two correctness points worth knowing before touching this code: (1) **a fresh `HttpClient` per level is
required**, not just per run — the window scale is negotiated once at the TCP handshake and does not change for
a connection's lifetime, so reusing a client across a level switch would silently measure the *previous* level's
window; and (2) the original level (read via `GetCurrentStateAsync` before the loop starts) is restored in a
`finally` around the whole loop, so it's put back on completion, cancellation, or exception alike, never left on
whatever the last-tested level happened to be. The ViewModel applies the "best" result as a mere *suggestion* to
the existing level `ComboBox`, not an auto-apply — and it does so only *after* calling the existing
`LoadStateAsync()` refresh, because that refresh's own `SelectedAutoTuningLevel` write would otherwise clobber
the suggestion if done in the other order.

**Each level is sampled `samplesPerLevel` times (default 5) and averaged, not measured once** — a single
download is noisy enough that it changed the "winner" between runs (user-reported 2026-07-06). `RunAsync` →
`MeasureLevelAsync` (loops samples, with a 300ms `InterSampleDelayMs` between them; no per-sample *settle* delay
is needed since the netsh level doesn't change within a level's samples) → `MeasureOnceAsync` (one raw download,
returns a private `SingleMeasurement`). `WindowsAutoTuningBenchmarkMath.Summarize` (pure, tested) turns the
successful samples into average/min/max; a level only reports `Success = false` if *every* sample in it failed.
`AutoTuningBenchmarkResult` carries `MinThroughputMbps`/`MaxThroughputMbps`/`SampleCount` alongside the average
specifically so the UI can show the spread, not just hide it behind a single number. `AutoTuningBenchmarkProgress`'s
`CompletedCount`/`TotalCount` count individual samples across *all* levels (e.g. `12/25` for 5 levels × 5
samples), not per-level.

**Downloads come from Hetzner's public speed-test files, not Cloudflare — Cloudflare was tried first and dropped
entirely after hitting two separate undocumented limits in quick succession on 2026-07-06.** Cloudflare's
`speed.cloudflare.com/__down?bytes=N` (the same endpoint Cloudflare's own official `speedtest` library and
speed.cloudflare.com use) hard-caps `bytes` at 100,000,000 — confirmed via `curl`: `99,999,999` → 200,
`100,000,000` → 403. Then, once averaging made each run issue up to 25 requests against that single endpoint, a
real 80MB/5-sample run got `429 Too Many Requests` on every level, with a `Retry-After: 2811` (~47 minute)
header — small (~1MB) requests still succeeded during the same lockout, meaning this reads as a bandwidth/volume
budget, not a plain request-count limit. An initial fix rotated between Cloudflare and Hetzner to spread the
load, but the user's follow-up ask was more direct: use a destination that plain isn't affected by this kind of
throttling, not just dilute the one that is. **A search for an equivalent official Google endpoint (the user's
suggested example) turned up nothing usable** — no documented Google download-speed API exists; a Google Cloud
Storage sample file returned 403 and `www.gstatic.com/generate_204` is a connectivity check (0 bytes), not a
bandwidth-test target. Cloudflare's `__down` is a dedicated, consumer-facing "speed test" tool, and reasonably
throttles the "many rapid automated requests" pattern much more tightly than a plain static-file host would;
**Hetzner's `https://<region>-speed.hetzner.com/100MB.bin` files are officially published "Test Files"** (a
large European hosting provider, unrelated to Cloudflare) served as ordinary static content (`Server: nginx`),
fetched here via HTTP `Range` requests since the file is fixed-size rather than query-parameterized — as of
2026-07-06 neither a size cap nor a 429 has been observed against it. `Targets` now lists only the 5 Hetzner
regions (fsn1/nbg1/hil/sin/ash); **the rotation index is `sampleIndex % Targets.Count` where `sampleIndex` resets
to 0 for every level**, not a global counter — every level's 1st sample always hits the same region, every
level's 2nd sample always hits the same (different) region, and so on, so a given region's geographic latency
doesn't bias the comparison *between* levels. A non-2xx response (429 or otherwise) is just a normal failed
sample — logged with the target's name prefixed (e.g. `[Hetzner sin] HTTP 429 ...`) and excluded from that
level's average, not fatal to the whole run. `MaxSafeTestSizeBytes` (90,000,000) is sized against Hetzner's own
104,857,600-byte file now, not Cloudflare's cap, but the constant and its safety margin carried over unchanged.
**Update (2026-07-06)**: the test size is no longer user-configurable. Real-world measurement showed the 20MB
default per-sample download exceeding `MeasureOnceAsync`'s 30s `HttpClient` timeout whenever a restricted
auto-tuning level (Disabled/HighlyRestricted/Restricted) shrank the receive window against a high-RTT Hetzner
target — the size `NumericUpDown` was removed from the UI and replaced with a fixed
`TcpTuningViewModel.BenchmarkTestSizeBytes = 5_000_000` (5MB) constant, small enough to stay within the timeout
even on the slowest level/target combination. Only `samplesPerLevel`'s `NumericUpDown` (max 5) remains
user-facing. Hetzner still hasn't been stress-tested at the scale that broke Cloudflare, so don't assume it's
rate-limit-proof at a larger fixed size either; re-verify with `curl`/a real run before loosening the constant.

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
whichever DNS IP is currently selected) and the standalone ネットワーク診断 tab (free-form host/IP input for both
ping and traceroute).

### Adapter list filtering (`WindowsNetworkAdapterFilter`, pure)

`NetworkInterface.GetAllNetworkInterfaces()` returns real adapters plus a lot of noise: NDIS filter/binding
**child interfaces** (`<parent>-QoS Packet Scheduler-0000`, `<parent>-WFP ...Filter-0000`), Wi-Fi Direct virtual
adapters, and `NotPresent` pseudo-devices. The DNS dropdown must match what `ncpa.cpl` shows. The pure filter keeps
Ethernet/Wireless80211 that are Present, drops a "binding child" if its Description is `<another adapter's
Description>-...` (structural — works for unknown antivirus/VPN LWFs without a hardcoded name list), and drops
`Wi-Fi Direct Virtual Adapter` by its (locale-stable, English) driver description. VPN TAP / Hyper-V vEthernet stay.

### Adapter details (`NetworkAdapterDetails`, read-only, both platforms)

Windows reads MAC address / link speed / media type / status in one call: `WindowsAdapterDetailsCommandBuilder`
runs `Get-NetAdapter -Name <adapter>` and emits `KEY=VALUE` lines (same locale-independent pattern as the TCP/DoH
state readers), parsed by `WindowsAdapterDetailsParser`. **macOS needs two calls, not one**, because
`networksetup`'s "network service" names (what `-listallnetworkservices` and the DNS dropdown use) aren't the BSD
device names `ifconfig` expects: `MacNetworkAdapterService.GetAdapterDetailsAsync` first runs
`networksetup -listnetworkserviceorder` and parses it (`MacNetworkServiceOrderParser`) into a service-name→device
(e.g. `en0`) map, then runs `ifconfig <device>` and parses *that* (`MacIfConfigParser`) for the actual details —
a service name alone can't be `ifconfig`'d directly.

### Ghost (disconnected) network device cleanup (`IGhostAdapterService`, Windows)

A section in the DNS tab lists **disconnected** network-class devices (leftover registry entries from removed USB
adapters / uninstalled VPNs) via `pnputil /enum-devices /disconnected /class Net /format xml` (XML for
locale-independent parsing) and removes one via `pnputil /remove-device "<InstanceId>"`. Windows' own WAN Miniport
virtual devices match the same filter, so `WindowsGhostAdapterParser` flags `Manufacturer` containing "Microsoft"
(→ UI shows a ⚠ warning, but deletion still runs on a single click — no confirmation gate). This uses pnputil's
proper PnP uninstall path rather than raw registry edits.

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
- Main view: `MainWindow` (880×620, min 760×500) is a sidebar `TabControl` — DNS 設定 / ネットワーク診断 /
  BBR2・TCP 調整 / メンテナンス (last two `IsVisible="{Binding IsWindows}"`) / バージョン — plus a persistent
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
