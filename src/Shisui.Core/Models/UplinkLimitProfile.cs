namespace Shisui.Core.Models;

/// <summary>
/// 回線ごとの上り速度計測の結果と、その回線へ適用する上限。AppSettings に保存する。
/// 同一回線 (AdapterGuid + GatewayMac) は上書きする。旧 settings.json には無いため、欠落は空リストとして読む。
/// </summary>
public sealed class UplinkLimitProfile
{
    /// <summary>既定経路の NIC の InterfaceGuid (「{XXXXXXXX-...}」大文字)。</summary>
    public string AdapterGuid { get; set; } = string.Empty;

    /// <summary>既定ゲートウェイの MAC (「AA-BB-CC-DD-EE-FF」大文字)。</summary>
    public string GatewayMac { get; set; } = string.Empty;

    public DateTimeOffset MeasuredAtUtc { get; set; }

    /// <summary>NIC 送信の実測容量 C (bps)。ポリシー値ではなく NIC ベース。</summary>
    public long CapacityBitsPerSecond { get; set; }

    /// <summary>適用するポリシー値 (bps)。変更不要・改善なしなど適用しない結果では null。</summary>
    public long? LimitPolicyBitsPerSecond { get; set; }

    /// <summary><see cref="UplinkMeasurementOutcome"/> の名前。</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>この回線で起動時に再適用する対象か。</summary>
    public bool IsEnabled { get; set; }

    public double? IdleRttP90Ms { get; set; }
    public double? LoadedRttP90Ms { get; set; }
    public double? LimitedRttP90Ms { get; set; }
    public double? IdleLossPercent { get; set; }
    public double? LoadedLossPercent { get; set; }
    public double? LimitedLossPercent { get; set; }
}
