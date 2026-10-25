using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// DoH 登録の整合性修復・確認と、明示的な解除コマンドを組み立てる。
/// </summary>
public static class WindowsDohCommandBuilder
{
    public const string FileName = "netsh";
    public const string EnableFileName = "powershell";

    /// <summary>
    /// 欠落を追加し、不整合な対象登録だけを修復する。既存の UDP fallback 禁止は維持する。
    /// </summary>
    public static string BuildEnableArguments(DnsServerSet servers, string dohTemplate)
    {
        if (!Uri.TryCreate(dohTemplate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || dohTemplate.Any(char.IsWhiteSpace) || dohTemplate.Any(char.IsControl))
            throw new ArgumentException("DoH には HTTPS テンプレートが必要です", nameof(dohTemplate));
        var script = WindowsDohStateCommandBuilder.BuildPrelude(CollectAddresses(servers)) +
            "$expected=" + WindowsDohStateCommandBuilder.Quote(dohTemplate) + ";" +
            "function Assert-DohType($e){if($e.DohTemplate -isnot [string] -or $e.AutoUpgrade -isnot [bool] -or $e.AllowFallbackToUdp -isnot [bool]){throw 'DoH property type is invalid'}};" +
            "$all=@(Get-DnsClientDohServerAddress -ErrorAction Stop);" +
            "$plan=@(foreach($ip in $targets){$found=@(Find-Doh $all $ip);" +
            "if($found.Count -gt 1){throw 'DoH registration is ambiguous'};" +
            "$missing=$found.Count -eq 0;$fallback=$true;$repair=$true;" +
            "if(-not $missing){$entry=$found[0];Assert-DohType $entry;$fallback=$entry.AllowFallbackToUdp;" +
            "$repair=$entry.DohTemplate -cne $expected -or -not $entry.AutoUpgrade};" +
            "[pscustomobject]@{Address=$ip;Missing=$missing;Fallback=$fallback;Repair=$repair}});" +
            "foreach($item in $plan){if($item.Missing){" +
            "Add-DnsClientDohServerAddress -ServerAddress $item.Address -DohTemplate $expected -AutoUpgrade $true -AllowFallbackToUdp $item.Fallback -ErrorAction Stop|Out-Null" +
            "}elseif($item.Repair){" +
            "Set-DnsClientDohServerAddress -ServerAddress $item.Address -DohTemplate $expected -AutoUpgrade $true -AllowFallbackToUdp $item.Fallback -ErrorAction Stop|Out-Null}};" +
            "$netsh=[IO.Path]::Combine([Environment]::SystemDirectory,'netsh.exe');" +
            "$native=@(& $netsh dnsclient set global doh=yes 2>&1);if($LASTEXITCODE -ne 0){throw 'DoH global enable failed'};" +
            "$after=@(Get-DnsClientDohServerAddress -ErrorAction Stop);" +
            "foreach($item in $plan){$found=@(Find-Doh $after $item.Address);if($found.Count -ne 1){throw 'DoH readback is not unique'};" +
            "$entry=$found[0];Assert-DohType $entry;" +
            "if($entry.DohTemplate -cne $expected -or -not $entry.AutoUpgrade -or $entry.AllowFallbackToUdp -ne $item.Fallback){throw 'DoH registration readback mismatch'}};" +
            "'DOHREPAIR=VERIFIED'";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    /// <summary>
    /// 設定対象の全サーバーの DoH 登録を削除する。グローバル設定 (doh=yes) は他の DoH 登録に
    /// 影響しうるため意図的に触らない。
    /// </summary>
    public static IReadOnlyList<string> BuildDisable(DnsServerSet servers) =>
        CollectAddresses(servers)
            .Select(address => $"dnsclient delete encryption server={WindowsDnsCommandBuilder.Quote(address)} protocol=doh")
            .ToList();

    /// <summary>設定対象アドレスを列挙する (null/空はスキップ)。状態読み取り側でも共有する。</summary>
    public static IReadOnlyList<string> CollectAddresses(DnsServerSet servers)
    {
        WindowsDnsCommandBuilder.ValidateServers(servers);

        var addresses = new List<string>();
        if (!string.IsNullOrWhiteSpace(servers.Ipv4Primary))
        {
            addresses.Add(servers.Ipv4Primary);
        }

        if (!string.IsNullOrWhiteSpace(servers.Ipv4Secondary))
        {
            addresses.Add(servers.Ipv4Secondary);
        }

        if (!string.IsNullOrWhiteSpace(servers.Ipv6Primary))
        {
            addresses.Add(servers.Ipv6Primary);
        }

        if (!string.IsNullOrWhiteSpace(servers.Ipv6Secondary))
        {
            addresses.Add(servers.Ipv6Secondary);
        }

        return addresses.Select(address => System.Net.IPAddress.Parse(address).ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
