namespace Shisui.Core.Models;

/// <summary>Wi-Fi の電源接続時設定を初めて適用する前の値。復元成功まで保持する。</summary>
public sealed class WifiPowerPlanSnapshot
{
    public Guid PlanGuid { get; set; }

    /// <summary>省電力の元の値 (1..3)。0 は適用不要のため保存せず、欠損・不正値として拒否する。</summary>
    public uint OriginalAcValue { get; set; }
}
