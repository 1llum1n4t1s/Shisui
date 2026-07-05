using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// DNS over HTTPS (DoH) の netsh dnsclient コマンド文字列を組み立てる純粋関数群 (プロセス起動は
/// 行わない、ユニットテスト対象)。netsh dnsclient add/delete encryption・set global は公式
/// ドキュメント通りの構文 (https://learn.microsoft.com/windows-server/administration/windows-commands/netsh-dnsclient)。
/// </summary>
public static class WindowsDohCommandBuilder
{
    public const string FileName = "netsh";

    /// <summary>
    /// 設定対象の全サーバーに DoH テンプレートを登録する (autoupgrade=yes で当該サーバーへの問い合わせを
    /// 強制的に DoH へ昇格。udpfallback=yes で DoH 解決が失敗した場合のみ平文にフォールバックし、
    /// 名前解決が完全に止まる事態を避ける)。最後にグローバル設定を doh=yes にして DoH 利用を許可する。
    /// </summary>
    public static IReadOnlyList<string> BuildEnable(DnsServerSet servers, string dohTemplate)
    {
        var commands = CollectAddresses(servers)
            .Select(address => $"dnsclient add encryption server={address} dohtemplate={dohTemplate} autoupgrade=yes udpfallback=yes")
            .ToList();

        commands.Add("dnsclient set global doh=yes");
        return commands;
    }

    /// <summary>
    /// 設定対象の全サーバーの DoH 登録を削除する。グローバル設定 (doh=yes) は他の DoH 登録に
    /// 影響しうるため意図的に触らない。
    /// </summary>
    public static IReadOnlyList<string> BuildDisable(DnsServerSet servers) =>
        CollectAddresses(servers)
            .Select(address => $"dnsclient delete encryption server={address} protocol=doh")
            .ToList();

    /// <summary>設定対象アドレスを列挙する (null/空はスキップ)。状態読み取り側でも共有する。</summary>
    public static IReadOnlyList<string> CollectAddresses(DnsServerSet servers)
    {
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

        return addresses;
    }
}
