using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.Tests;

/// <summary>
/// TCP 設定の検証テストと ViewModel テストで共有する <see cref="TcpSettingsSnapshot"/> の生成ヘルパー。
/// 既定値は「全テンプレート BBR2・自動チューニング Normal（ローカル設定）」で、
/// <see cref="Bbr2Status.Partial"/> のときだけ Compat を CUBIC にして部分適用状態を表す。
/// </summary>
internal static class TcpSettingsSnapshotFactory
{
    internal static TcpSettingsSnapshot CreateSnapshot(
        Bbr2Status bbr2 = Bbr2Status.Enabled,
        string autoTuningLevel = "Normal",
        string autoTuningLevelGroupPolicy = "",
        string autoTuningLevelEffective = "Local") =>
        new(
            bbr2,
            new Dictionary<TcpGlobalOption, string>(),
            autoTuningLevel,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Internet"] = "BBR2",
                ["InternetCustom"] = "BBR2",
                ["Datacenter"] = "BBR2",
                ["DatacenterCustom"] = "BBR2",
                ["Compat"] = bbr2 == Bbr2Status.Partial ? "CUBIC" : "BBR2",
            },
            autoTuningLevelGroupPolicy,
            autoTuningLevelEffective);
}
