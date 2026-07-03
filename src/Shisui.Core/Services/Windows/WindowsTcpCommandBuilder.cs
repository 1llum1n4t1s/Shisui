using Shisui.Core.Interfaces;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// BBR2 輻輳制御・TCP グローバル設定のコマンド文字列を組み立てる純粋関数群。
/// congestionprovider=bbr2 は netsh interface tcp set supplemental の公式ドキュメント記載値。
/// </summary>
public static class WindowsTcpCommandBuilder
{
    public const string FileName = "netsh";

    private static readonly string[] SupplementalTemplates = ["Internet", "InternetCustom", "Datacenter", "DatacenterCustom", "Compat"];

    public static IReadOnlyList<string> BuildEnableBbr2()
    {
        var commands = SupplementalTemplates
            .Select(template => $"int tcp set supplemental template={template} congestionprovider=BBR2")
            .ToList();

        commands.Add("int ipv6 set global loopbacklargemtu=disable");
        commands.Add("int ipv4 set global loopbacklargemtu=disable");
        return commands;
    }

    public static IReadOnlyList<string> BuildRevertBbr2ToDefault()
    {
        var commands = SupplementalTemplates
            .Select(template => $"int tcp set supplemental template={template} congestionprovider=default")
            .ToList();

        commands.Add("int ipv6 set global loopbacklargemtu=enabled");
        commands.Add("int ipv4 set global loopbacklargemtu=enabled");
        return commands;
    }

    public static string BuildSetGlobalOption(TcpGlobalOption option, bool enabled)
    {
        var value = enabled ? "enabled" : "disabled";
        var key = option switch
        {
            TcpGlobalOption.Rsc => "rsc",
            TcpGlobalOption.EcnCapability => "ecncapability",
            TcpGlobalOption.Timestamps => "timestamps",
            TcpGlobalOption.Rss => "rss",
            TcpGlobalOption.FastOpen => "fastopen",
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
        return $"int tcp set global {key}={value}";
    }

    public const string ShowGlobalStatus = "int tcp show global";
}
