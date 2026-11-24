using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 上り回線の計測 (無負荷 → 容量 → 二分探索 → 検証)。適用はしない。
/// 変更ゲートは呼び出し側が取得済みとする。開始時に <see cref="IUplinkLimitService.BeginMeasurementAsync"/>、
/// 終了時に必ず finally で <see cref="IUplinkLimitService.EndMeasurementAsync"/> (CancellationToken.None) を呼び、
/// 結果が <see cref="UplinkMeasurementOutcome.Recommended"/> のときだけ適用用を復元せず、それ以外は元の値へ復元する。
/// 失敗・拒否・キャンセルも例外にせず report で返す (OperationCanceledException は Cancelled として返す)。
/// </summary>
public interface IUplinkMeasurementService
{
    Task<UplinkMeasurementReport> MeasureAsync(IProgress<UplinkMeasurementProgress>? progress = null, CancellationToken ct = default);
}
