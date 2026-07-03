using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// netsh の DNS 設定コマンド文字列を組み立てる純粋関数群 (プロセス起動は行わない、ユニットテスト対象)。
/// netsh interface ipv4/ipv6 set/add dnsservers は公式ドキュメント通りの構文
/// (https://learn.microsoft.com/windows-server/administration/windows-commands/netsh-interface)。
/// </summary>
public static class WindowsDnsCommandBuilder
{
    public const string FileName = "netsh";

    public static IReadOnlyList<string> BuildApply(string adapterName, DnsServerSet servers)
    {
        var commands = new List<string>();
        var name = Quote(adapterName);

        if (servers.HasIpv4)
        {
            commands.Add($"interface ipv4 set dnsservers name={name} source=static address={servers.Ipv4Primary} register=primary validate=no");
            if (!string.IsNullOrWhiteSpace(servers.Ipv4Secondary))
            {
                commands.Add($"interface ipv4 add dnsservers name={name} address={servers.Ipv4Secondary} index=2 validate=no");
            }
        }

        if (servers.HasIpv6)
        {
            commands.Add($"interface ipv6 set dnsservers name={name} source=static address={servers.Ipv6Primary} register=primary validate=no");
            if (!string.IsNullOrWhiteSpace(servers.Ipv6Secondary))
            {
                commands.Add($"interface ipv6 add dnsservers name={name} address={servers.Ipv6Secondary} index=2 validate=no");
            }
        }

        return commands;
    }

    public static IReadOnlyList<string> BuildResetToAutomatic(string adapterName)
    {
        var name = Quote(adapterName);
        return
        [
            $"interface ipv4 set dnsservers name={name} source=dhcp",
            $"interface ipv6 set dnsservers name={name} source=dhcp",
        ];
    }

    /// <summary>
    /// netsh は CommandLineToArgvW ではなく生コマンドラインを独自再パースするため、
    /// スペースを含みうる値はここで netsh 流の二重引用符で囲む。
    /// </summary>
    private static string Quote(string value) => $"\"{value}\"";
}
