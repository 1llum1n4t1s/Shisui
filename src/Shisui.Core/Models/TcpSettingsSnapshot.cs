using Shisui.Core.Interfaces;

namespace Shisui.Core.Models;

public enum Bbr2Status
{
    /// <summary>5 つのテンプレート全てが BBR2。</summary>
    Enabled,

    /// <summary>いずれのテンプレートも BBR2 でない (既定の輻輳制御)。</summary>
    Disabled,

    /// <summary>一部のテンプレートだけ BBR2 (中途半端な状態)。</summary>
    Partial,

    /// <summary>取得できなかった。</summary>
    Unknown,
}

/// <summary>
/// 現在の TCP 関連設定のスナップショット。BBR2 の適用状況と、各グローバルオプションの生値
/// ("Enabled" / "Disabled" / "Allowed" / "Default" / "" 等、ロケール非依存の英語トークン) を持つ。
/// <para>
/// <c>InitialRto</c> は Internet テンプレートの初期再送タイムアウト (ミリ秒の文字列。既定 3000、Shisui の目標は 1000)、
/// <c>MaxSynRetransmissions</c> は同テンプレートの SYN 再送回数の上限 (目標は 4)。
/// どちらも <c>Get-NetTCPSetting -SettingName Internet</c> の値で、読み取れなかったときは空文字列。
/// </para>
/// </summary>
public sealed record TcpSettingsSnapshot(
    Bbr2Status Bbr2,
    IReadOnlyDictionary<TcpGlobalOption, string> GlobalOptions,
    string AutoTuningLevel = "",
    IReadOnlyDictionary<string, string>? CongestionProviders = null,
    string AutoTuningLevelGroupPolicy = "",
    string AutoTuningLevelEffective = "",
    string InitialRto = "",
    string MaxSynRetransmissions = "")
{
    public static readonly TcpSettingsSnapshot Unknown =
        new(Bbr2Status.Unknown, new Dictionary<TcpGlobalOption, string>());

    public string GetOptionValue(TcpGlobalOption option) =>
        GlobalOptions.TryGetValue(option, out var value) ? value : string.Empty;

    public IReadOnlyDictionary<string, string> GetCongestionProviders() =>
        CongestionProviders ?? new Dictionary<string, string>();
}
