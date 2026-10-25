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

On Windows, `ProcessCommandExecutor` does not pass those short executable names to the OS search path. The known
system commands used by Shisui (`netsh`, `powershell`, `ipconfig`, `pnputil`, `nbtstat`, `route`, and `netcfg`)
are resolved to absolute paths below `Environment.SystemDirectory` (PowerShell uses its fixed
`WindowsPowerShell\v1.0` subdirectory), and unknown relative executables are rejected. The child working directory
is also System32. This is required because the whole app is elevated and an unqualified executable search could
otherwise select a user-writable binary before the Windows system copy.

アダプター名を含む PowerShell スクリプトは `-EncodedCommand` で渡し、接続名に含まれる生の `"` が
外側の `-Command "..."` を終端しないようにします。スクリプト内ではシングルクォートを二重化して
文字列リテラルとして扱います。NetAdapter cmdlet の `-Name` はワイルドカードとして解釈されるため、
全件から `OrdinalIgnoreCase` で一意に完全一致させ、後続 cmdlet へ名前を渡す場合は
`[WildcardPattern]::Escape` します。executor の詳細ログでは Base64 ではなく復号したスクリプトを記録します。

On macOS, read-only adapter discovery and ping/traceroute run through `ProcessCommandExecutor` without elevation.
Only DNS mutation and cache flush use `MacElevatedCommandExecutor`, which re-wraps the already-quoted
`fileName + " " + arguments` shell command inside an AppleScript string literal (backslash/quote escaping only)
and invokes `osascript` via `ArgumentList` internally (a normal argv-parsing tool, so `ArgumentList` is correct
there). The launcher itself is fixed to `/usr/bin/osascript` rather than resolved through `PATH`.

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
executors terminate the whole process tree both on cancellation and on other post-start exceptions, so a failed
wait or output read does not leave a privileged command running in the background.
終了要求を受け付けない子プロセスも、実際の終了までは待機を続けます。生バイト読取り経路では、キャンセル済みの出力を破棄先へ読み進めてパイプ詰まりを避けます。macOS のイベント読取りはそのまま継続し、どちらも呼び出し元の処理中状態と変更操作の排他を保持します。

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
reads all DoH registrations once, rejects duplicate/malformed target records, adds missing target entries with
`Add-DnsClientDohServerAddress`, and repairs mismatched template/AutoUpgrade using `Set-DnsClientDohServerAddress`.
Already matching entries need no setter. Existing `AllowFallbackToUdp=false` remains false; new entries retain
the existing product fallback policy of true. It then runs absolute System32 `netsh dnsclient set global doh=yes`
and reads all target entries back with strict template/bool/fallback checks. Only a unique verification marker
confirms repair; this verifies stored registration, not TLS transport. Disabling still runs `netsh dnsclient
delete encryption server=<ip> protocol=doh` per IP (global `doh` is left alone — it can affect other
registrations). See the next section for the DoT sibling toggle, which uses the same `netsh dnsclient` family
but is architecturally different because it has no state-read cmdlet.

**The checkbox reflects real OS state, it is not a saved preference.** `WindowsDohStateCommandBuilder` /
`WindowsDohStateParser` read type-preserving JSON from `Get-DnsClientDohServerAddress` and classify
`DohStatus` against the expected preset template and typed bool properties. Missing, duplicate or malformed
target rows are Unknown; an AutoUpgrade flag alone cannot establish Enabled. `DnsSettingsViewModel.RefreshDohStateAsync` runs on startup
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
and then refreshes the manual TCP tab's state badges. Optional Windows `IIpStackResetService` first runs
IPv4 and IPv6 reset, followed by the configuration below. It does not capture or restore previous NIC
settings or global tuning. Before each reset, a separate PowerShell command probes the corresponding fixed
NSI IPv4/IPv6 `\26` key. Only when read/write succeeds but adding DELETE fails, the owner is Administrators,
the DACL is protected and canonical with ordinary Allow ACEs only, and WRITE_DAC is already available, it adds
a non-inherited DELETE-only Administrators ACE. It preserves other ACL entries, owner, group, and protection,
ignoring only the DACL AutoInherited bookkeeping flag that Windows can add when saving the descriptor.
It checks for intervening changes, reads the result back, and closes every handle before netsh runs. The comparison
and write are not atomic, so an external ACL change in that interval remains a limitation. Unrecognized
ACLs remain unchanged and are reported as failures; no ownership or privilege takeover is attempted.
The reset helper can return `ERROR_SUPPRESS_OUTPUT` (15017) even after every reset succeeds; netsh maps this
to process exit 1. Only for this reset command, exit 1 with empty stderr and output exactly matching all
34 success messages plus the restart notice is accepted as complete. The expected output comes from the
System32 netiohlp string resources, preserving category order and empty category names; an unknown resource
shape, missing/extra output, or any error keeps the original failure. The raw exit code is retained and the
separate completion decision is logged. Other nonzero exits remain failures while reconfiguration
continues. Completion still requires a PC restart for Windows to apply the reset fully.
Each external command has a 120-second timeout; cancellation, exceptions, or an unknown process exit stop
later mutations through `RequiresOperationAbort`. There is no recovery journal, startup recovery, restore
button, or recovery-dependent UI lock. Legacy IP backup properties in settings JSON are ignored.
Under the existing shared mutation lease, allowlisted
maintenance with `RunBeforeOneClickConfiguration` runs before DHCP/DNS/TCP/NIC settings. IPsec repair queries
both Main Mode and Quick Mode SAs before deleting captured Quick Mode then Main Mode SAs. It does not modify
IPsec policies. No SAs or unavailable support is an explicit skip; query and partial deletion failures remain
failures while subsequent repairs continue. New SAs may be established, so a permanently empty SA list is not
the completion criterion. Next, optional Windows
`IWinsockResetService` first checks the native Winsock SPI catalog, including hidden and standalone LSPs;
on a 64-bit OS it also checks the 32-bit catalog with `WSCEnumProtocols32`. Only complete, nonempty catalogs
whose protocol chains are all base providers (`ChainLen=1`) permit `netsh winsock reset`. Layered providers
cause an explicit protective skip; unreadable or malformed catalogs fail without resetting. A 32-bit process
on a 64-bit OS cannot prove absence and refuses to reset. Every invocation checks afresh and logs the decision.
The manual maintenance command remains unchanged. Next, optional `IWinHttpCacheService` uses an owned NO_PROXY
session and `WinHttpResetAutoProxy` with cache-only flags `0x2000C` (OUT_OF_PROC, SWPAD_ALL, SCRIPT_CACHE).
No proxy-setting setter is called. Calls within 30 seconds of a previous attempt are skipped: prior acceptance
is an explicit success-without-call, while prior failure remains incomplete. Zero means API acceptance,
not measured cache removal. Both repairs precede DHCP, Winsock send-buffer autotuning and TCP/NIC tuning;
their failure is retained while later repairs still run. Neither service reacquires the shared mutation gate.
Before DNS changes, optional Windows `IDhcpLeaseService`
releases and renews DHCP IPv4 on the selected NIC only. IPv6 requires a usable Preferred address with
PrefixOrigin=Dhcp; fixed IP and SLAAC-only IPv6 are skipped. Unknown DHCP state is not modified and is reported
as a failed verification rather than a successful exclusion. Name, GUID and InterfaceIndex
must agree before each operation and after query. Release has a 15-second timeout; renew always runs with a
fresh independent 60-second token, even after release failure/cancellation. Bounded readback checks a usable
DHCP address and restoration of any previously present default gateway; failure remains visible in results.
Adapter names that could expand to multiple adapters or alter quoting are rejected. macOS omits the service.
The remaining sequence switches the selected preset to
Cloudflare standard, enables DoH if the preset supports it, flushes the DNS cache, and — when
`INetworkMaintenanceService` is available (Windows; injected as `INetworkMaintenanceService? = null` the same
way the other Windows-only services are, so macOS silently skips this step) — also runs only maintenance commands
whose `MaintenanceCommandDefinition.IncludeInOneClickOptimization` flag is true, omitting repairs already run
before configuration: NetBIOS name-cache purge/reload and conditional WINS name re-registration,
all-interface IPv4 ARP-cache flush (`netsh interface ipv4 delete arpcache`), IPv4/IPv6 destination-cache flushes,
the IPv6 neighbor-discovery cache flush, and `netsh winsock set autotuning on` so old tweak tools cannot leave
Winsock's independent send-buffer autotuning disabled.
The allowlist includes `route-clear` (`route /f`), but the ordinary maintenance loop defers it until the final mutation described below.
The allowlist also restores UDP URO and USO to `default` with two separate `netsh interface udp set global` calls,
so an unsupported option cannot prevent the other from running. It deliberately does not run `udp reset` or force
offload `enabled`; both default-restoration actions are individually available in the maintenance tab.
The allowlist also disables PCI Express Link State Power Management for AC power in the current power plan.
It additionally requests maximum Wi-Fi performance on AC power in that same active plan, preserving DC.
Wi-Fi changes require a persisted original AC value before writing; reapplication retains that original.
A manual-only maintenance action restores the current plan's original AC value, refuses external-value
conflicts and removes its record only after readback and successful settings save. Plan changes, failed reads,
or incomplete verification cannot silently succeed. This does not force driver power policy or switch plans.
It reads the existing values before writing and verifies AC is off and the DC value is unchanged afterwards;
a failed initial read must not cause a write. It neither switches power plans nor writes the DC value.
This action is separately available in the maintenance tab. Its UI description explains that it affects PCI Express
devices beyond the selected NIC and may increase power consumption and heat; streaming improvement is not guaranteed.
The command sequence uses the documented [powercfg AC-value and activation commands](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options).
WINS registration runs through the same conditional path for manual and one-click execution: CIM must confirm
an IP-enabled NIC with NetBIOS not disabled and a valid configured WINS server before fixed System32 `nbtstat -RR`
is invoked. Missing/unreadable configuration is not treated as a confirmed absence, and native failure codes
propagate through PowerShell. No WINS server or NetBIOS configuration is changed.
Both conditional commands have a 45-second linked timeout. The executor attempts to terminate the child
process tree, but termination is not guaranteed. Timeout results set `RequiresOperationAbort`: one-click
and manual batch callers stop subsequent mutations, retain the failure, and advise restarting the PC.
This is an explicit allowlist: dynamic DNS registration and HTTP.sys log-buffer/server-response-cache operations
remain available in the maintenance tab but are deliberately excluded
from one-click because their applicability depends on name-registration or HTTP-server use; one-click also
switches to public DNS. They are evaluated separately from latency tuning. DNS cache flushing runs through the
final DNS client verification service when available, or once through `IDnsCacheService` otherwise. Finally
— only when `ITcpTuningService` is available (Windows, same optional-injection pattern) — restores all five TCP
templates and any other user-configured TCP parameters with the official `netsh int tcp reset`, then deliberately
enables BBR2 for all five templates and runs explicit fallback resets for common tweak-tool targets (RSS, RSC, ECN, timestamps,
initial RTO, non-SACK resiliency, SYN retries, Fast Open/fallback, HyStart, PRR, pacing, and force-window-scaling),
then calls `ApplyLowLatencyGlobalOptionsAsync` to explicitly enable global RSS, ECN, timestamps, Fast Open and
Fast Open fallback, and disable global RSC through six commands. These overrides prioritize latency; their effects
depend on the peer, network path and application.
`EnableTcpPacingAsync` then sets PC-wide TCP `pacingprofile=always`, regardless of the selected adapter's medium.
The wired off/always/off measurement conditions do not restrict product applicability.
It uses System32 netsh, checks native exit codes, and verifies a unique `pacingprofile=always` token in the
`set global` lines of `int tcp dump`. This confirms stored configuration, not per-connection pacing or latency.
Any prior `RequiresOperationAbort` prevents this setter and subsequent changes; the pacing result has the same abort boundary.
The policy favors transmission smoothing and reduced queue pressure; off/always/off measurements with fixed BBR2
did not consistently improve median latency. It neither requires BBR2 nor guarantees improvement. Manual reset remains default.
The manual reset operation still
restores all options to their defaults without these one-click overrides. These global states do not prove
individual NIC capabilities or latency improvement. Disabling RSC can increase CPU usage and reduce receive throughput.
Microsoft's [RSC guidance](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-choose-nic#receive-segment-coalescing-rsc)
allows disabling it for low-latency workloads that benefit; that Server guidance does not establish a measured improvement
for every Windows 11 game. The one-click policy prioritizes latency, and includes the selected NIC gaming profile before device/name cleanup. Individual NIC apply/restore operations remain available. It
removes only the per-interface legacy tweak values `TcpAckFrequency`, `TCPNoDelay`, and `TcpDelAckTicks` from
`HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\<GUID>` before applying `TcpAckFrequency=1`
as a DWORD to the selected NIC only. `SetTcpAckFrequencyAsync` resolves the connection name exactly and uniquely,
opens only the existing OS-provided interface GUID key, and checks the stored type/value and NIC identity after mutation.
`GetTcpAckFrequencyAsync` distinguishes an absent value (Windows default) from an unreadable or unsupported value (Unknown)
using strict ASCII markers. This is stored configuration; a restart is required to change runtime ACK behavior.
After one-click device/name cleanup, the manual TCP tab reloads adapters and selects the renamed target before refreshing its state.
One-click also
disables IPv4/IPv6 loopback Large MTU as part of the BBR2 configuration, and resets receive-window auto-tuning to Normal. The registry command emits
`REMOVED=N`, never deletes an interface key, and deliberately does not touch the unrelated MSMQ `TCPNoDelay` value.
Because Microsoft documents these delayed-ACK registry changes as requiring a restart, the one-click description
and success status tell Windows users to restart the PC. The explicit netsh commands make
partial failures visible in the execution log and recover supported settings even if the aggregate reset fails.
After the reset and explicit defaults, it enables both `rack` and `taillossprobe` on Internet, InternetCustom,
Datacenter and DatacenterCustom unless a fresh read confirms both are already enabled. In that case it reports
"already enabled, no change" without a write: Windows build 26300.9457 rejects even individual RACK/TLP writes
as unsupported while exposing both as enabled. This narrow readback accepts only exact known English/Japanese
labels and explicit `enabled` values; unknown labels, missing/duplicate fields or a failed query cannot skip a write.
Compat's loss-recovery settings are left to the preceding TCP reset; its BBR2
enablement remains unchanged. The paired loss-recovery command follows Microsoft's
[netsh guidance](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface).
The TCP tab exposes selected-NIC ACK frequency enable/default actions and stored-value badges, aggregate TCP reset,
global-option reset, legacy ACK/Nagle cleanup, and paired RACK/TLP enablement
as separate commands; one-click must not be the only UI path to any setting mutation it performs.
The TCP/UDP/power normalization phase, including BBR2/loss-recovery enablement and the narrow PCI Express AC power setting, is limited to documented TCP/UDP/power state and those three specifically named legacy
per-interface values. It does not delete arbitrary registry values,
alter BCD, or replace power plans. Selected NIC advanced properties are changed only through the gaming profile allowlist below. DoT is deliberately left untouched (see the DoT section above: DoH
measured slightly faster and more consistent, so there's little benefit to enabling both). On Windows, one-click
calls `INetworkAdapterNameService.CleanupAsync` after the NIC profile with the selected connection name: it removes every
disconnected PnP network-device registration and, when possible, strips the selected live adapter's numeric suffix.
This must run after every operation that uses the old connection name; if a rename succeeds, the new name is persisted
before adapters are reloaded. Disabled live devices are preserved, but unplugged USB LAN and dock NIC registrations
are intentionally included and the button description warns that Windows will redetect them when reconnected.
After DHCP, DNS, TCP, NIC settings and device/name cleanup, one-click runs the deferred `route-clear` once as its final network-configuration mutation while retaining the same shared lease.
This clears the PC-wide route table and interrupts connectivity; users are told to restart afterwards. Command failure or an exception is logged and makes the overall operation fail.
When the optional Windows `IDnsClientRepairService` is present, one-click captures a runtime-only System event
RecordId/XML SHA256 baseline before its first setter. It replaces the earlier cache flush with a final cache
rebuild and current NRPT/hosts/Windows resolution/fresh-event verification after route cleanup. The first event
range starts at the pre-mutation baseline; retries continue from the previous captured end, including waiting gaps.
At most two attempts allow postconfiguration stabilization, while first-attempt event IDs, record IDs and ranges
remain visible. Lost/replaced/unreadable boundaries and unknown event schemas never establish zero new errors
and cannot be cleared merely by a later successful probe.
An unresolved probe after route cleanup remains a failed verification with an undetermined cause, even when
`VerificationDeferred` requests a later check. Every failed/unknown DNS verification sets the persisted primitive
`DnsHealthVerificationPending`; the next startup performs one new baseline/rebuild/read/probe under the shared
mutation gate, with deferred classification disabled. Only confirmed success clears the flag. If saving that
clear fails, memory rolls back to pending and the UI separates successful DNS checks from failed settings save.
No pending-state UI lock or configuration snapshot/restore is introduced. Missing service/macOS retain the old
cache-flush path. NRPT/hosts/certificates/TLS/proxy settings and Tailscale service state are never reset by this check.
Other destructive maintenance actions (per-adapter MTU restoration and the remaining commands in the 「ファイアウォール・スタックリセット」category)
remain excluded. Since PCI Express AC power management / UDP defaults / BBR2 and RACK/TLP enablement / TCP global-option reset / loopback Large MTU / auto-tuning /
cache-maintenance commands / route-table clearing are global, not scoped
to the selected adapter (unlike the DNS and gaming NIC changes), the button's description text calls this out explicitly for
multi-NIC environments.

Before reporting completion, one-click reads a fresh TCP snapshot inside the same mutation lease.
`TcpSettingsVerifier` checks BBR2 across all five templates, global RSS enabled/RSC disabled, Internet ECN and
timestamps enabled, global Fast Open enabled, and the effective Internet auto-tuning level Normal, including
the GroupPolicy override. A mismatch, unavailable value, incomplete snapshot, or read failure is logged as a failed verification,
without losing prior command results or preventing settings persistence/adapter cleanup. Manual BBR2 enablement
and auto-tuning changes use the same verifier. Command failures remain failures even when readback matches.
RACK/TLP writes still report command acceptance only; only the no-change path confirms the pre-existing state.
Fast Open fallback is command-acceptance-only; Fast Open readback does not verify fallback.
The PCI Express action separately verifies its AC/DC power values. UDP and other settings remain command-acceptance-only. MTU is not changed by one-click.

Because `SelectedPreset`'s setter would trigger `OnSelectedPresetChanged`'s fire-and-forget
`RefreshDohStateAsync` call (racing against this method's own `await`ed call at the end), the preset switch here
assigns the backing field directly and raises the needed `OnPropertyChanged` notifications by hand instead of
going through the property setter (2026-07-06, found via `/rere` review — the two `RefreshDohStateAsync` calls
could complete out of order and leave the DoH checkbox showing a stale state).

### Reading current state is locale-independent (PowerShell cmdlets and netsh dump)

`netsh int tcp show global` / `show supplemental` values are stable English tokens (`enabled` / `bbr2`) but the
**labels are localized** (Japanese Windows shows 「Receive-Side Scaling 状態」 etc.), so parsing that text breaks
on non-English Windows. So the "current state" badges in the BBR2/TCP tab read state through
`WindowsTcpStateCommandBuilder` → a one-line PowerShell command that emits `KEY=VALUE` lines from
`Get-NetOffloadGlobalSetting` (RSS/RSC) + `Get-NetTCPSetting` (ECN/Timestamps/CongestionProvider). The keys are
English-fixed by us and the enum values are English, so the whole output is locale-independent;
`WindowsTcpStateParser` (pure, unit-tested) parses it. **BBR2 status = all 5 templates' `CongestionProvider`**
(Enabled / Partial / Disabled only when all five named templates are present exactly once; missing, duplicate,
malformed or mixed named/legacy output becomes Unknown). Historical unnamed fixtures are accepted only with exactly
five values. The PowerShell command uses `ErrorActionPreference='Stop'` so cmdlet failures cannot masquerade
as successful reads. **FastOpen** has no PowerShell property, so the same script invokes the absolute System32
`netsh.exe interface tcp dump` and reads its locale-independent, replayable command keywords. It accepts only a
zero exit code, exactly one `set global` line and exactly one `fastopen=` token whose value is `enabled` or
`disabled` (case-insensitive), emitting canonical `FASTOPEN=Enabled` or `FASTOPEN=Disabled`. Missing, unsupported,
malformed or ambiguous output, a nonzero exit code or an exception produces an empty `FASTOPEN=` value while
preserving the other state reads. This unavailable value fails one-click verification. No localized `show global`
labels are parsed for this read. **loopbacklargemtu** is still not tracked in the live state; the BBR2 enable
button sets it without a readback check.

**Auto-tuning level** rides in the same one-line script: `AUTOTUNE=` holds `AutoTuningLevelLocal`,
`AUTOTUNE_POLICY=` holds `AutoTuningLevelGroupPolicy`, and `AUTOTUNE_SOURCE=` holds `AutoTuningLevelEffective`
(Local/GroupPolicy, not a tuning level). The badge and verifier use the effective value selected by that source;
an unknown source never silently falls back to Local. The selector still shows the editable local setting.
See [the Microsoft property contract](https://learn.microsoft.com/en-us/windows/win32/fwp/wmi/nettcpipprov/msft-nettcpsetting).
Reading these auto-tuning properties adds no process spawn beyond the state script and its Fast Open dump. **MTU
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
property reset is offered only for the selected adapter's packet-error or discard-rate warning. The global problem-device
count only guides the user to Device Manager and driver checks. Reset targets only the currently selected adapter via
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
neither Windows parser touches it directly. `WindowsPingCommandBuilder` uses a sequential `.NET Ping` loop
with a one-second timeout and approximately one-second start spacing. Every attempt emits numeric `STATUS`/`RTT`,
including failed probes, so missing replies cannot be bridged when calculating jitter. Counts are bounded to 1–100.
`WindowsTraceRouteCommandBuilder` gets
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

The diagnostic UI offers 4/30/100 probes and cancellation. Windows results include loss, min/max/average,
nearest-rank p95 of successful RTTs, and mean absolute differences of consecutive successful RTTs; failed or
malformed probes break the jitter chain. Missing statistics are null, not zero. Measurements use OS routing,
not the adapter selector; raw RTT is millisecond-resolution and ICMP loss may reflect response throttling.
Summaries are timestamped in the UI and file log. macOS does not fabricate unavailable per-probe statistics.

### Gaming NIC profile (`IGamingNetworkProfileService`, Windows)

AutoOptimization exposes the gaming NIC profile only through one-click; there is no individual apply/restore card. `WindowsGamingNetworkProfileService` retains its journal and standalone apply/restore APIs, which own the shared mutation gate from read through persistence and verification. One-click calls `ApplyWithinMutationLeaseAsync` after TCP readback and before name cleanup while retaining its existing lease, so the service does not reacquire the non-reentrant gate. The caller must hold the same shared lease through the entire operation. Empty results or exceptions become explicit one-click failures, and ordinary failed NIC results remain failures.
Only physical Ethernet (type 6) or Wi-Fi (type 71) is eligible. Standard driver-exposed `*RSS`, `*LsoV2IPv4`,
and `*LsoV2IPv6` target 1; IPv4 IP checksum and IPv4/IPv6 TCP/UDP checksum offload target 3 (Tx and Rx enabled).
Targets and documented value domains are keyword-specific and shared by parsing, command validation,
journal validation, apply/restore conflict checks and readback. Missing properties are not synthesized;
driver support for the original and target values must be verified. Existing binary power-property journals remain compatible.
`WindowsGamingNetworkProfilePolicy` selects exact
keywords by media/provider/PCI identity: standard `*InterruptModeration` for either media, `*EEE` only for Ethernet,
`EnableGreenEthernet` / `GigaLite` / `PowerSavingMode` for physical Realtek Ethernet identified by provider name,
PCI vendor 10EC or USB vendor 0BDA, and `LowPowerEnable` / `UAPSDSupport` only for MediaTek Wi-Fi (`MediaTek, Inc.`
and PCI vendor 14C3). These Realtek power properties are changed only when the driver actually exposes the exact key
with binary 0/1 values. Their binary semantics were verified against `RTL8168H.ndi.NT` in driver INF
1168.27.50.920; the MediaTek keys were verified against the installed RZ616 driver INF (3.5.0.1349).
`AMSDUTx=0` additionally requires physical Wi-Fi, exact provider `MediaTek, Inc.` and PCI VEN_14C3/DEV_0616
with a device-token boundary. Its generated query/apply/restore policy checks the original bool/integer CIM types
before accepting the property; string attributes cannot satisfy this additional allowlist. Both published binary
values 0/1 are required. Existing journal, conflict, restoration and stored-value verification paths apply.
This favors reducing transmit aggregation without claiming consistent measured latency improvement.
`AMSDURx` is not modified or reset: disabling receive aggregation has no confirmed aggregation-wait reduction
mechanism for this driver. Transmit and receive operations have different responsibilities; this does not assert
that changing receive capability can never affect an AP. See Microsoft's
[WDI transmit](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wdi-transmit-operations-and-offloads)
and [WDI receive](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/wdi-receive-operations-and-offloads) contracts.
This is not a generic manufacturer-independent Wi-Fi power switch. Missing properties are reported;
malformed values, wrong media/provider combinations and identity changes cannot authorize writes or restore.
The additional `*FlowControl` and `AdvancedEEE` targets are 0 and require physical Ethernet with a
`PCI\VEN_10EC&` device identity; provider-name-only and USB matches do not authorize these two changes.
Flow Control uses its documented 0/1/2/3 domain and requires driver support for the original value and 0;
Advanced EEE requires both binary values. Installed RTL8125BG INF 1125.027.50.0919 exposes Flow Control 0/3
and Advanced EEE 0/1. Standard `*UdpRsc` targets 0 on physical Ethernet or Wi-Fi only when the driver exposes
the exact property and both 0/1 values. [URO](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/udp-rsc-offload)
is separate from TCP RSC and the OS-level UDP URO/USO defaults. Missing keywords are never created.
Query, apply and restore use the same generated media/vendor policy expression.
PowerShell emits strictly validated JSON (JsonDocument, without reflection), uses exact names after wildcard escaping,
and sets the selected property CIM object with `-NoRestart`. GUID, interface description and the property's
InstanceID are rechecked before each write. Persistent journal models use the source-generated JSON context.
Original changed values are persisted in AppSettings before writing; reapplication never replaces the original.
Restoration touches only the journal's allowlisted entries and refuses external-value conflicts. Failed/cancelled
changes retain restoration information. A journal is removed only after successful readback and settings save.
Readback confirms saved configuration only. UI requires a PC restart after apply/restore and never claims a
performance improvement. Wi-Fi power savings are exchanged for potential latency reduction, not a guaranteed win;
battery usage and heat may increase. The operation leaves virtual NICs, offloads beyond the exact allowlist, speed/duplex, buffers,
band preference, channel width and roaming settings alone.

Rationale: [Microsoft low-latency NIC guidance](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-performance-tuning-nics)
recommends enabling static offloads and RSS for multiple receive streams. These address host packet-processing
cost and do not establish a game or WAN latency improvement. Global RSS configuration and saved NIC values
are verified separately; neither proves post-reboot driver behavior or bypasses a global DisableTaskOffload setting.
The same guidance describes interrupt moderation's CPU/latency tradeoff; [Intel EEE documentation](https://edc.intel.com/content/www/us/en/design/products/ethernet/adapters-and-devices-user-guide/other-power-options/)
describes the low-power transition latency. Neither supports a universal WAN latency or packet-loss improvement.
[Microsoft Wi-Fi power management](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/wi-fi-power-management-for-modern-standby-platforms)
describes wireless power/latency tradeoffs; exact MediaTek registry values come from the installed driver INF,
not from assumed equivalence with Intel or Realtek settings.

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

- **Avalonia** with Fluent theme. Authoritative package versions are in `src/Shisui.UI/Shisui.UI.csproj`.
- MVVM via CommunityToolkit.Mvvm 8.4.2. Compiled bindings (`AvaloniaUseCompiledBindingsByDefault=true`) — every
  `.axaml` needs `x:DataType`; binding-path typos become build errors.
- **Design system is a copy of `C:\Users\IMT\dev\Lhamiel`'s "macOS Tahoe" look**: `Resources/Themes.axaml` holds
  the Light/Dark palette (`Color.*` / `Brush.*`, Apple-blue accent, translucent glass `Brush.Container`), and
  `App.axaml` holds the shared styles — a **left-sidebar `TabControl.sidebar` ControlTemplate** (floating rounded
  glass panel), the `HeaderedContentControl` glass-card template used for every settings section, and unified
  corner radii. The window uses `TransparencyLevelHint="AcrylicBlur"` + `ExperimentalAcrylicBorder`. When adding UI,
  reuse `HeaderedContentControl` for cards and the `h1`/`caption` TextBlock classes rather than inventing styles.
- Main view: `MainWindow` (880×620, min 760×500) is a sidebar `TabControl` — 自動最適化 (initially selected) / DNS 設定 /
  ネットワーク診断 / BBR2・TCP 調整 / メンテナンス (the TCP and maintenance tabs use
  `IsVisible="{Binding IsWindows}"`) / バージョン — plus a persistent
  実行ログ card fed by each tab ViewModel's `CommandExecuted` event, one `UserControl` per tab. Sidebar tab icons
  are hand-drawn with plain Avalonia primitives (`Ellipse`/`Line`/`Path`/`Polyline`/`Rectangle`+`RotateTransform`)
  bound to `{DynamicResource Brush.FG1}` — no icon font/package, no `PathIcon`. Before committing hand-computed
  coordinates to XAML, preview the shape first (write it as standalone SVG, publish via the `Artifact` tool,
  screenshot it) — a first-draft wrench icon for メンテナンス read as a magnifying glass once rendered and was
  redrawn as an 8-tooth gear before it ever reached `MainWindow.axaml`.
- The optimization explanation lists each setting and its ON/OFF/reset/delete action. MainWindowViewModel aggregates running states across tabs, disables the entire content including logs, and displays a themed indeterminate progress overlay. One-click reports its current phase through OneClickProgressText and clears it in finally. User close requests are blocked during processing; programmatic and OS shutdown remain available.
  During cancellable Ping measurement, the overlay exposes the existing cancel command while normal controls stay disabled.
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
  Only a genuinely missing file permits initial saving. Read failures (including invalid or null JSON) are logged and
  block saves for that service instance, preserving the original file and NIC/Wi-Fi journals until the problem is fixed
  and the app is restarted. UI save failures are reported separately from completed OS changes; update preferences are
  restored when saving fails. NIC DNS read failures remain in the adapter list with a distinct status and exception log.
- Logging: `SuperLightLogger` NuGet package (same as RealTimeTranslator), via `LoggerBootstrap`. Logs to
  `AppPaths.LogsDirectory`.
  Session startup records the app version, OS/build, architecture, runtime, culture and Windows elevation state.
  One-click optimization also records its operation ID and selected adapter; an async-local scope ties its
  commands and synthetic verification results together without mixing concurrent background reads.
  Both executors record command IDs, actual start/end timestamps, elapsed time, resolved executable/PID,
  exit codes and both output streams regardless of success. Cancellation and exceptions are separate diagnostic
  events with full exception details; the raw-stream executor also retains captured partial output.
  UI notifications reference the execution ID instead of pretending their delayed notification time is the
  execution time. Synthetic verification results retain stdout and stderr in the file; screen logs are unchanged.
  Logs contain network addresses, adapter names and diagnostic output; they stay local and should be reviewed
  before sharing. Do not pass passwords, tokens or other secrets in command arguments or output.
- Single-instance: `SingleInstanceGuard` uses a `FileShare.None`-locked file under the OS temp directory, not a
  named `Mutex` — named mutexes with `Local\`/`Global\` prefixes are a Windows-only convention and behave
  differently (or not at all) cross-platform, so a plain file lock is used for portability instead.
