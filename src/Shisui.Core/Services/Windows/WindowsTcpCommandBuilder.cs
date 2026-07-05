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

    /// <summary>
    /// 受信ウィンドウ自動調整 (auto-tuning) レベルを設定する。値は公式ドキュメント記載の 5 種
    /// (disabled/highlyrestricted/restricted/normal/experimental)。
    /// </summary>
    public static string BuildSetAutoTuningLevel(AutoTuningLevel level)
    {
        var value = level switch
        {
            AutoTuningLevel.Disabled => "disabled",
            AutoTuningLevel.HighlyRestricted => "highlyrestricted",
            AutoTuningLevel.Restricted => "restricted",
            AutoTuningLevel.Normal => "normal",
            AutoTuningLevel.Experimental => "experimental",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
        return $"int tcp set global autotuninglevel={value}";
    }

    /// <summary>
    /// 指定アダプタの IPv4/IPv6 MTU を設定する (store=persistent で再起動後も維持)。ジャンボフレームを
    /// 使う場合は 9000 前後の値を指定する (実際に通るかは NIC ドライバ側のジャンボフレーム対応にも依存)。
    /// </summary>
    public static IReadOnlyList<string> BuildSetMtu(string adapterName, int mtu)
    {
        var name = Quote(adapterName);
        return
        [
            $"interface ipv4 set subinterface name={name} mtu={mtu} store=persistent",
            $"interface ipv6 set subinterface name={name} mtu={mtu} store=persistent",
        ];
    }

    /// <summary>
    /// netsh は CommandLineToArgvW ではなく生コマンドラインを独自再パースするため、
    /// スペースを含みうる値はここで netsh 流の二重引用符で囲む (WindowsDnsCommandBuilder と同じ方針)。
    /// </summary>
    private static string Quote(string value) => $"\"{value}\"";
}
