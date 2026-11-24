using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 1 区間分の上り負荷と観測 (NIC 送信統計 1 秒ごと・ICMP 10Hz) を行い、生データを返す。
/// 統計・判定は含めない。キャンセルは OperationCanceledException。測定先の失敗は例外にせず
/// <see cref="UplinkProbeStatus.TargetError"/> で返す。
/// </summary>
public interface IUplinkLoadProbe
{
    Task<UplinkProbeResult> RunAsync(UplinkProbeRequest request, CancellationToken ct = default);
}
