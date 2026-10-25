using System.Net;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>DNS 検査の PowerShell を生成する。OS の読み書きは実行側だけで行う。</summary>
public static class WindowsDnsClientRepairCommandBuilder
{
    public static string BuildCapture(string marker) => WindowsPowerShellArgumentEncoder.Encode(
        Common + "\ntry { $anchor=Get-Anchor; Write-Output ('" + Literal(marker)
        + "|BASE|' + $anchor.Id + '|' + $anchor.Hash) } catch { Write-Output 'DNS 検査開始境界を取得できません。'; exit 1 }");

    public static string BuildRepair(DnsServerSet servers, long recordId, string hash,
        bool inspectDohEvents, bool deferFailedResolution, string marker)
    {
        WindowsDnsCommandBuilder.ValidateServers(servers);
        if (recordId <= 0 || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("DNS 検査境界が不正です。");
        var addresses = new[] { servers.Ipv4Primary, servers.Ipv4Secondary, servers.Ipv6Primary, servers.Ipv6Secondary }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => "'" + IPAddress.Parse(x!.Trim()) + "'");
        var prefix = $"\n$scopeId={recordId}; $scopeHash='{hash}'; $targets=@({string.Join(",", addresses)}); "
            + $"$inspectDoh=${inspectDohEvents.ToString().ToLowerInvariant()}; $allowDefer=${deferFailedResolution.ToString().ToLowerInvariant()}; $marker='{Literal(marker)}';\n";
        return WindowsPowerShellArgumentEncoder.Encode(Common + prefix + Repair);
    }

    private static string Literal(string value) => value.Replace("'", "''");

    private const string Common = """
        $ErrorActionPreference='Stop'
        $OutputEncoding=[Console]::OutputEncoding=New-Object Text.UTF8Encoding $false
        function Get-Hash($event) {
            $sha=[Security.Cryptography.SHA256]::Create()
            try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($event.ToXml())))).Replace('-','') }
            finally { $sha.Dispose() }
        }
        function Get-Anchor {
            $items=@(Get-WinEvent -LogName System -MaxEvents 1 -ErrorAction Stop)
            if ($items.Count -ne 1 -or $null -eq $items[0].RecordId -or [long]$items[0].RecordId -le 0) { throw 'anchor' }
            return @{ Id=[long]$items[0].RecordId; Hash=(Get-Hash $items[0]) }
        }
        function Confirm-Anchor($id,$hash) {
            $items=@(Get-WinEvent -LogName System -FilterXPath "*[System[EventRecordID=$id]]" -MaxEvents 2 -ErrorAction Stop)
            if ($items.Count -ne 1 -or [long]$items[0].RecordId -ne $id -or (Get-Hash $items[0]) -cne $hash) { throw 'anchor changed' }
        }
        function Get-FreshEvents($from,$to) {
            if ($to -lt $from) { throw 'event range reversed' }
            if ($to -eq $from) { return @() }
            try {
                $items=@(Get-WinEvent -LogName System -FilterXPath "*[System[(EventRecordID > $from) and (EventRecordID <= $to) and Provider[@Name='Microsoft-Windows-DNS-Client'] and (EventID=1012 or EventID=1023 or EventID=8048)]]" -MaxEvents 257 -ErrorAction Stop)
                if ($items.Count -gt 256) { throw 'event overflow' }
                return $items
            } catch {
                if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return @() }
                throw
            }
        }
        function Confirm-Nrpt {
            $rules=@(Get-DnsClientNrptRule -ErrorAction Stop)
            $policies=@(Get-DnsClientNrptPolicy -Effective -ErrorAction Stop)
            foreach ($rule in @($rules)+@($policies)) {
                if ($null -eq $rule -or @($rule.Namespace).Count -eq 0) { throw 'NRPT shape' }
                foreach ($ns in @($rule.Namespace)) { if ([string]::IsNullOrWhiteSpace([string]$ns)) { throw 'NRPT namespace' } }
                foreach ($server in @($rule.NameServers)) {
                    if ([string]::IsNullOrWhiteSpace([string]$server)) { continue }
                    foreach ($address in ([string]$server -split '[;,\s]+')) {
                        $parsed=$null
                        if (-not [Net.IPAddress]::TryParse($address,[ref]$parsed)) { throw 'NRPT server' }
                    }
                }
            }
            Write-Output ('NRPT 現在値を読み取りました（規則 ' + $rules.Count + '、有効ポリシー ' + $policies.Count + '）。')
        }
        function Confirm-Hosts {
            $base=(Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters' -ErrorAction Stop).DataBasePath
            if ([string]::IsNullOrWhiteSpace([string]$base)) { $base=[IO.Path]::Combine([Environment]::SystemDirectory,'drivers\etc') }
            $base=[Environment]::ExpandEnvironmentVariables([string]$base)
            if ($base -notmatch '^[a-zA-Z]:[\\/]' -or $base.StartsWith('\\')) { throw 'hosts local path' }
            $path=[IO.Path]::GetFullPath([IO.Path]::Combine($base,'hosts'))
            for ($parent=[IO.FileInfo]$path; $null -ne $parent;) {
                if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'hosts reparse' }
                if ($parent -is [IO.FileInfo]) { $parent=$parent.Directory } else { $parent=$parent.Parent }
            }
            if (-not [IO.File]::Exists($path)) {
                # Exists はアクセス拒否も false にするため、欠落以外は例外として区別する。
                try { $null=[IO.File]::GetAttributes($path) } catch [IO.FileNotFoundException] {
                    Write-Output 'hosts ファイルはありません（欠落だけでは名前解決エラーと判定しません）。'; return
                } catch [IO.DirectoryNotFoundException] {
                    Write-Output 'hosts ファイルはありません（欠落だけでは名前解決エラーと判定しません）。'; return
                }
                throw 'hosts unreadable'
            }
            $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
            try {
                if ($stream.Length -gt 1048576) { throw 'hosts too large' }
                $buffer=New-Object byte[] 4096
                $total=0
                while (($read=$stream.Read($buffer,0,$buffer.Length)) -gt 0) {
                    $total+=$read
                    if ($total -gt 1048576) { throw 'hosts too large' }
                    for ($i=0;$i -lt $read;$i++) { if ($buffer[$i] -eq 0) { throw 'hosts encoding unknown' } }
                }
            } finally { $stream.Dispose() }
            Write-Output 'hosts 現在ファイルを読み取れました（OS による使用確認とは別です）。'
        }
        """;

    private const string Repair = """
        try { Confirm-Anchor $scopeId $scopeHash } catch {
            Write-Output '開始境界が消失・変更したか、System ログを読めないため検証不能です。'
            Write-Output ($marker+'|RESULT|FAIL'); exit 1
        }
        $fromAnchor=@{ Id=$scopeId; Hash=$scopeHash }; $coverageFailed=$false
        for ($attempt=1;$attempt -le 2;$attempt++) {
            Write-Output ('DNS クライアント検査 ' + $attempt + '/2')
            $otherFailed=$coverageFailed; $resolutionFailed=$false; $endAnchor=$null
            try { Confirm-Anchor $fromAnchor.Id $fromAnchor.Hash; Confirm-Anchor $scopeId $scopeHash }
            catch { $otherFailed=$true; $coverageFailed=$true; Write-Output '検査範囲の開始境界が消失・変更したか、System ログを読めません。' }
            try { Clear-DnsClientCache -ErrorAction Stop; Write-Output 'DNS キャッシュを再構築しました。' }
            catch { $otherFailed=$true; Write-Output 'DNS キャッシュの再構築に失敗しました。' }
            try { Confirm-Nrpt } catch { $otherFailed=$true; Write-Output 'NRPT の現在値を検証できません。規則は変更していません。' }
            try { Confirm-Hosts } catch { $otherFailed=$true; Write-Output 'hosts の現在ファイルを検証できません。内容は変更していません。' }
            try {
                $answers=@(Resolve-DnsName -Name example.com -Type A -DnsOnly -NoHostsFile -QuickTimeout -ErrorAction Stop)
                $valid=$false
                foreach ($answer in $answers) {
                    $ip=$null
                    if ([Net.IPAddress]::TryParse([string]$answer.IPAddress,[ref]$ip) -and $ip.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) { $valid=$true }
                }
                if (-not $valid) { throw 'no A answer' }
                Write-Output 'Windows DNS 名前解決を確認しました。'
            } catch { $resolutionFailed=$true; Write-Output 'Windows DNS 名前解決の動作確認に失敗しました。' }
            try {
                    $endAnchor=Get-Anchor
                    if ($endAnchor.Id -lt $fromAnchor.Id) { throw 'event range reversed' }
                    Write-Output ('System ログ検査範囲: RecordId (' + $fromAnchor.Id + ', ' + $endAnchor.Id + ']。')
                    $events=@(Get-FreshEvents $fromAnchor.Id $endAnchor.Id)
                    foreach ($event in $events) {
                        [xml]$xml=$event.ToXml()
                        $eventId=0
                        if (-not [int]::TryParse([string]$xml.Event.System.EventID,[ref]$eventId) -or $eventId -notin @(1012,1023,8048)) { throw 'event type' }
                        $eventRecord=0L
                        if (-not [long]::TryParse([string]$xml.Event.System.EventRecordID,[ref]$eventRecord) -or $eventRecord -le $fromAnchor.Id -or $eventRecord -gt $endAnchor.Id -or $eventRecord -ne [long]$event.RecordId) { throw 'event record range' }
                        $data=@{}
                        foreach ($entry in @($xml.Event.EventData.Data)) {
                            $key=[string]$entry.Name
                            if ([string]::IsNullOrWhiteSpace($key) -or $data.ContainsKey($key)) { throw 'event fields' }
                            $data[$key]=[string]$entry.InnerText
                        }
                        if ($eventId -eq 8048) {
                            if (-not $inspectDoh) { continue }
                            $ip=$null
                            if (-not $data.ContainsKey('Server') -or -not [Net.IPAddress]::TryParse($data['Server'],[ref]$ip)) { throw 'DoH target unknown' }
                            if ($targets.Count -eq 0) { throw 'DoH target missing' }
                            if ($targets -contains $ip.ToString()) {
                                if (-not $data.ContainsKey('Template') -or -not $data.ContainsKey('Error') -or [string]::IsNullOrWhiteSpace($data['Error'])) { throw 'DoH event shape' }
                                $otherFailed=$true; Write-Output ('対象 DNS サーバーの DoH/TLS エラーを検出しました（EventID 8048、RecordId ' + $eventRecord + '）。')
                            }
                        } elseif ($eventId -eq 1012) {
                            if (-not $data.ContainsKey('ErrorCode') -or -not $data.ContainsKey('Location') -or -not $data.ContainsKey('Context')) { throw 'hosts event shape' }
                            $otherFailed=$true; Write-Output ('hosts 読み込みエラーを検出しました（EventID 1012、RecordId ' + $eventRecord + '）。')
                        } else {
                            if (-not $data.ContainsKey('RuleName') -or -not $data.ContainsKey('ErrorCode')) { throw 'NRPT event shape' }
                            $otherFailed=$true; Write-Output ('NRPT 規則エラーを検出しました（EventID 1023、RecordId ' + $eventRecord + '）。')
                        }
                    }
                    Confirm-Anchor $fromAnchor.Id $fromAnchor.Hash
                    Confirm-Anchor $endAnchor.Id $endAnchor.Hash
                    Confirm-Anchor $scopeId $scopeHash
            } catch { $otherFailed=$true; $coverageFailed=$true; Write-Output 'System ログの連続性または DNS イベントの対象・型を検証できません。' }
            if ($null -ne $endAnchor) { $fromAnchor=$endAnchor }
            if (-not $otherFailed -and -not $resolutionFailed) {
                if ($attempt -eq 2) { Write-Output '初回は不調でしたが、キャッシュ再構築後の再確認時は正常です。' }
                Write-Output '今回の現在値と記録済みイベントの検査範囲は正常です。検査後に遅れて記録されるイベント、全 MagicDNS や DoH/TLS 実通信の正常性を保証する検査ではありません。'
                Write-Output ($marker+'|RESULT|OK'); exit 0
            }
            if ($attempt -eq 1) { Write-Output '設定反映の安定化を 1 秒待ち、キャッシュを再構築して一度だけ再確認します。'; Start-Sleep -Milliseconds 1000; continue }
            if (-not $otherFailed -and $resolutionFailed -and $allowDefer) {
                Write-Output '名前解決の動作確認未完了（原因未確定、再起動後再確認）。'
                Write-Output ($marker+'|RESULT|DEFER'); exit 1
            }
        }
        Write-Output '現在の異常または検証不能が続いています。'
        Write-Output ($marker+'|RESULT|FAIL'); exit 1
        """;
}
