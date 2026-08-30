# Shisui アーキテクチャ詳細

このファイルは `CLAUDE.md` から退避した詳細。毎ターンは要らないが、該当領域を触る前には必ず該当節を読む。

---

## Architecture

### Project Structure

- **Shisui.Core** — Interfaces, models, and all OS-interacting services. No UI dependency.
  - `Interfaces/` — `INetworkAdapterService`, `IDnsConfigurationService`, `IDohConfigurationService`,
    `IDotConfigurationService`, `IDnsCacheService`, `ITcpTuningService`, `INetworkMaintenanceService`,
    `IGhostAdapterService`, `INetworkAdapterNameService`, `INetworkDiagnosticsService`,
    `ILegacyNetworkDiagnosticsService`,
    `INetworkMutationGate`,
    `ICommandExecutor`, `ISettingsService`.
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
  - `Services/MacOS/` — `networksetup`/`dscacheutil`/`ifconfig`-backed implementations. Read-only adapter and
    diagnostics commands use `ProcessCommandExecutor`; DNS mutation and cache flush use
    `MacElevatedCommandExecutor`, which wraps only those commands through `osascript -e 'do shell script "..."
    with administrator privileges'` (macOS apps should not request a blanket admin launch the way Windows apps
    do via manifest).
- **Shisui.UI** — Avalonia desktop app. Views/ViewModels (CommunityToolkit.Mvvm `[ObservableProperty]` /
  `[RelayCommand]`), DI setup in `App.axaml.cs`.
- **Shisui.Tests** — MSTest unit tests. Covers command builders, ViewModel outcome reporting, the DNS preset
  catalog's exact IP values, and cancellation of a harmless child process. It deliberately does **not** execute
  network-mutating commands against the host.

### Command execution model — string arguments, not `ArgumentList`

`ICommandExecutor.RunAsync(string fileName, string arguments, ...)` takes a single pre-formatted argument
**string**, not `ArgumentList`/`string[]`. This is deliberate: `netsh` does not use standard `CommandLineToArgvW`
argv parsing — it re-parses the raw command line itself and expects literal `name="Adapter Name"` quoting.
Passing adapter names through .NET's `ProcessStartInfo.ArgumentList` causes the runtime to escape embedded quotes
(`\"`), which breaks netsh's own tokenizer. Each `*CommandBuilder` (Windows and macOS) is responsible for quoting
correctly for its target executable's own parsing convention. DNS address values are parsed as IPv4/IPv6 before
command construction, and embedded double quotes in raw netsh string arguments are rejected.

On macOS, read-only adapter discovery and ping/traceroute run through `ProcessCommandExecutor` without elevation.
Only DNS mutation and cache flush use `MacElevatedCommandExecutor`, which re-wraps the already-quoted
`fileName + " " + arguments` shell command inside an AppleScript string literal (backslash/quote escaping only)
and invokes `osascript` via `ArgumentList` internally (a normal argv-parsing tool, so `ArgumentList` is correct
there).

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
warning). `DecodeConsoleOutput` is `internal` + unit-tested (`ProcessCommandExecutorDecodeTests`). Both process
executors terminate the whole process tree before rethrowing `OperationCanceledException`, so a canceled wait
does not leave a privileged command running in the background.

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

On macOS, the DI container registers `ProcessCommandExecutor` as the shared `ICommandExecutor` for read-only
services and registers `MacElevatedCommandExecutor` by concrete type for `MacDnsConfigurationService` and
`MacDnsCacheService` only.

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
