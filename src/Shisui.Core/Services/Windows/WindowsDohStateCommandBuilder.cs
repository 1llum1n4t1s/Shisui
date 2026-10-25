using System.Net;

namespace Shisui.Core.Services.Windows;

/// <summary>対象 DoH 登録を型を保持した JSON で読み取り、期待テンプレートとの厳密な比較へ渡す。</summary>
public static class WindowsDohStateCommandBuilder
{
    public const string FileName = "powershell";

    internal static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    internal static string BuildPrelude(IReadOnlyList<string> addresses)
    {
        if (addresses.Count == 0)
            throw new ArgumentException("DNS アドレスが必要です", nameof(addresses));
        var targets = string.Join(",", addresses.Select(address => Quote(IPAddress.Parse(address).ToString())));
        return "$ErrorActionPreference='Stop';" +
            "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            "$targets=@(" + targets + ");" +
            "function Find-Doh($entries,$ip){$expectedIp=[Net.IPAddress]::Parse($ip);" +
            "foreach($entry in $entries){$parsed=$null;" +
            "if($entry.ServerAddress -is [string] -and [Net.IPAddress]::TryParse($entry.ServerAddress,[ref]$parsed) -and $parsed.Equals($expectedIp)){$entry}}};";
    }

    public static string BuildArguments(IReadOnlyList<string> serverAddresses)
    {
        var script = BuildPrelude(serverAddresses) +
            "$all=@(Get-DnsClientDohServerAddress -ErrorAction Stop);" +
            "$rows=@(foreach($ip in $targets){foreach($entry in @(Find-Doh $all $ip)){" +
            "[pscustomobject]@{ServerAddress=$entry.ServerAddress;DohTemplate=$entry.DohTemplate;" +
            "AutoUpgrade=$entry.AutoUpgrade;AllowFallbackToUdp=$entry.AllowFallbackToUdp}}});" +
            "ConvertTo-Json -InputObject @($rows) -Compress -Depth 3";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }
}
