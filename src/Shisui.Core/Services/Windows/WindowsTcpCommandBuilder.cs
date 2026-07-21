using Shisui.Core.Interfaces;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// BBR2 輻輳制御・TCP グローバル設定のコマンド文字列を組み立てる純粋関数群。
/// congestionprovider=bbr2 は netsh interface tcp set supplemental の公式ドキュメント記載値。
/// </summary>
public static class WindowsTcpCommandBuilder
{
    public const string FileName = "netsh";

    /// <summary>ユーザー構成の TCP パラメーターを削除し、Windows の既定値へ戻す公式 netsh コマンド。</summary>
    public const string ResetAllToDefault = "int tcp reset";

    public static readonly IReadOnlyList<string> SupplementalTemplates =
        ["Internet", "InternetCustom", "Datacenter", "DatacenterCustom", "Compat"];

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

    public static IReadOnlyList<string> BuildSetCongestionProviders(IReadOnlyDictionary<string, string> providers)
    {
        if (providers.Count != SupplementalTemplates.Count ||
            SupplementalTemplates.Any(template => !providers.ContainsKey(template)))
        {
            throw new ArgumentException("5つ全てのTCPテンプレートが必要です", nameof(providers));
        }

        return SupplementalTemplates.Select(template =>
        {
            var provider = providers[template];
            if (string.IsNullOrWhiteSpace(provider) || provider.Any(c => !char.IsLetterOrDigit(c)))
            {
                throw new ArgumentException("輻輳制御プロバイダー名が不正です", nameof(providers));
            }

            return $"int tcp set supplemental template={template} congestionprovider={provider}";
        }).ToList();
    }

    /// <summary>
    /// よくあるチューニングツールが変更する TCP グローバル設定を、Windows が現在のバージョンで定義する
    /// システム既定値へ戻す。<see cref="ResetAllToDefault"/> の後にも個別実行し、全体リセットが一部失敗した環境でも
    /// 復元できる項目を増やし、実行ログから失敗項目を判別できるようにする。
    /// </summary>
    public static IReadOnlyList<string> BuildRevertGlobalOptionsToDefault() =>
    [
        "int tcp set global rss=default",
        "int tcp set global rsc=default",
        "int tcp set global ecncapability=default",
        "int tcp set global timestamps=allowed",
        "int tcp set global initialrto=3000",
        "int tcp set global nonsackrttresiliency=default",
        "int tcp set global maxsynretransmissions=2",
        "int tcp set global fastopen=default",
        "int tcp set global fastopenfallback=default",
        "int tcp set global hystart=default",
        "int tcp set global prr=default",
        "int tcp set global pacingprofile=default",
        "int tcp set heuristics forcews=default",
    ];

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

    public static string BuildRevertGlobalOptionToDefault(TcpGlobalOption option) => option switch
    {
        TcpGlobalOption.Rsc => "int tcp set global rsc=default",
        TcpGlobalOption.EcnCapability => "int tcp set global ecncapability=default",
        // Microsoftの既定値はAllowed。timestampsはdefaultではなく文書化された既定トークンを明示する。
        TcpGlobalOption.Timestamps => "int tcp set global timestamps=allowed",
        TcpGlobalOption.Rss => "int tcp set global rss=default",
        TcpGlobalOption.FastOpen => "int tcp set global fastopen=default",
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

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
