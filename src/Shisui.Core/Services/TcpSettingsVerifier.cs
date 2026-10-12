using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>設定コマンドの終了結果とは別に、読み戻せた TCP の実状態を検証する純粋関数。</summary>
public static class TcpSettingsVerifier
{
    /// <summary>グローバルオプションを期待する有効・無効状態と比較し、取得不能も失敗として扱う。</summary>
    public static CommandExecutionResult VerifyGlobalOption(TcpSettingsSnapshot snapshot, TcpGlobalOption option, bool enabled)
    {
        var actual = snapshot.GetOptionValue(option);
        var expected = enabled ? "Enabled" : "Disabled";
        var name = option.ToString().ToUpperInvariant();
        var success = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return new(success, $"{name} 適用後確認", success ? 0 : -1, $"{name}={actual}, Expected={expected}",
            success ? string.Empty : string.IsNullOrWhiteSpace(actual)
                ? $"{name} の状態を確認できませんでした。"
                : $"{name} が期待する {expected} ではありません。OS・NIC の対応とポリシー設定を確認してください。");
    }

    public static CommandExecutionResult VerifyBbr2Enabled(TcpSettingsSnapshot snapshot)
    {
        var success = snapshot.Bbr2 == Bbr2Status.Enabled;
        var detail = string.Join(Environment.NewLine,
            snapshot.GetCongestionProviders().Select(pair => $"{pair.Key}={pair.Value}"));
        return new(success, "BBR2 適用後確認", success ? 0 : -1, detail,
            success ? string.Empty : snapshot.Bbr2 == Bbr2Status.Unknown
                ? "5 テンプレートすべての輻輳制御を確認できませんでした。"
                : "5 テンプレートすべてが BBR2 ではありません。");
    }

    public static CommandExecutionResult VerifyAutoTuning(TcpSettingsSnapshot snapshot, AutoTuningLevel expected)
    {
        var effective = GetEffectiveAutoTuningLevel(snapshot);
        var success = string.Equals(effective, expected.ToString(), StringComparison.OrdinalIgnoreCase);
        var detail = $"Internet: Local={snapshot.AutoTuningLevel}, GroupPolicy={snapshot.AutoTuningLevelGroupPolicy}, " +
                     $"Source={snapshot.AutoTuningLevelEffective}, Effective={effective}";
        return new(success, "受信ウィンドウ適用後確認 (Internet)", success ? 0 : -1, detail,
            success ? string.Empty : string.IsNullOrEmpty(effective)
                ? "受信ウィンドウ自動調整の実効値を確認できませんでした。"
                : $"受信ウィンドウ自動調整の実効値が {expected} ではありません。ポリシー設定も確認してください。");
    }

    /// <summary>Effective はレベルではなく採用元。取得不能時はローカル値に推測でフォールバックしない。</summary>
    public static string GetEffectiveAutoTuningLevel(TcpSettingsSnapshot snapshot) =>
        snapshot.AutoTuningLevelEffective.ToUpperInvariant() switch
        {
            "LOCAL" => snapshot.AutoTuningLevel,
            "GROUPPOLICY" => snapshot.AutoTuningLevelGroupPolicy,
            _ => string.Empty,
        };
}
