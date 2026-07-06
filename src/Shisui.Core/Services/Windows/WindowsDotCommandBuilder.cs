using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// DNS over TLS (DoT) の netsh dnsclient コマンド文字列を組み立てる純粋関数群 (プロセス起動は
/// 行わない、ユニットテスト対象)。DoH と同じ add/delete encryption ファミリで、テンプレートの
/// パラメータ名が dohtemplate= の代わりに dothost=&lt;host&gt;:&lt;port&gt; になる点だけが違う
/// (公式ドキュメント https://learn.microsoft.com/windows-server/administration/windows-commands/netsh-dnsclient)。
/// アドレス列挙は DoH と共有 (<see cref="WindowsDohCommandBuilder.CollectAddresses"/>)。
/// </summary>
public static class WindowsDotCommandBuilder
{
    public const string FileName = "netsh";

    /// <summary>DoT の既定ポート (RFC 7858)。</summary>
    public const int DefaultPort = 853;

    /// <summary>
    /// 設定対象の全サーバーに DoT ホストを登録し (autoupgrade=yes で当該サーバーへの問い合わせを強制的に
    /// DoT へ昇格、udpfallback=yes で失敗時のみ平文にフォールバック)、グローバル設定を dot=yes にする。
    /// </summary>
    public static IReadOnlyList<string> BuildEnable(DnsServerSet servers, string dotHost)
    {
        var commands = WindowsDohCommandBuilder.CollectAddresses(servers)
            .Select(address => $"dnsclient add encryption server={WindowsDnsCommandBuilder.Quote(address)} dothost={dotHost}:{DefaultPort} autoupgrade=yes udpfallback=yes")
            .ToList();

        commands.Add("dnsclient set global dot=yes");
        return commands;
    }

    /// <summary>
    /// 設定対象の全サーバーの DoT 登録を削除する。グローバル設定 (dot=yes) は他の DoT 登録に
    /// 影響しうるため意図的に触らない (DoH の BuildDisable と同じ方針)。
    /// </summary>
    public static IReadOnlyList<string> BuildDisable(DnsServerSet servers) =>
        WindowsDohCommandBuilder.CollectAddresses(servers)
            .Select(address => $"dnsclient delete encryption server={WindowsDnsCommandBuilder.Quote(address)} protocol=dot")
            .ToList();
}
