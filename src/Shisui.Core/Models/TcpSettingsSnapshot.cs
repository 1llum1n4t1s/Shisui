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
/// </summary>
public sealed record TcpSettingsSnapshot(
    Bbr2Status Bbr2,
    IReadOnlyDictionary<TcpGlobalOption, string> GlobalOptions,
    string AutoTuningLevel = "")
{
    public static readonly TcpSettingsSnapshot Unknown =
        new(Bbr2Status.Unknown, new Dictionary<TcpGlobalOption, string>());

    public string GetOptionValue(TcpGlobalOption option) =>
        GlobalOptions.TryGetValue(option, out var value) ? value : string.Empty;
}
