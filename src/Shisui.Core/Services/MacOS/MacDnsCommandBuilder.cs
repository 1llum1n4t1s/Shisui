using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// networksetup の DNS 設定コマンド文字列を組み立てる純粋関数群 (プロセス起動は行わない、ユニットテスト対象)。
/// networksetup -setdnsservers はサービス名 1 つに対し IPv4/IPv6 混在の順序付きリストを渡せる。
/// </summary>
public static class MacDnsCommandBuilder
{
    public const string FileName = "networksetup";

    public static string BuildApply(string serviceName, DnsServerSet servers)
    {
        List<string> addresses = [];
        if (!string.IsNullOrWhiteSpace(servers.Ipv4Primary)) addresses.Add(servers.Ipv4Primary);
        if (!string.IsNullOrWhiteSpace(servers.Ipv4Secondary)) addresses.Add(servers.Ipv4Secondary);
        if (!string.IsNullOrWhiteSpace(servers.Ipv6Primary)) addresses.Add(servers.Ipv6Primary);
        if (!string.IsNullOrWhiteSpace(servers.Ipv6Secondary)) addresses.Add(servers.Ipv6Secondary);

        var quotedAddresses = addresses.Select(MacShellQuote.Quote);
        return $"-setdnsservers {MacShellQuote.Quote(serviceName)} {string.Join(' ', quotedAddresses)}";
    }

    /// <summary>
    /// "Empty" は networksetup が予約している、手動 DNS 設定を消して DHCP 供給に戻すためのキーワード。
    /// </summary>
    public static string BuildResetToAutomatic(string serviceName) =>
        $"-setdnsservers {MacShellQuote.Quote(serviceName)} Empty";

    public static string BuildGetCurrent(string serviceName) =>
        $"-getdnsservers {MacShellQuote.Quote(serviceName)}";
}
