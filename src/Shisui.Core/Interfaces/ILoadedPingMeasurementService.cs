using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 現在の TCP 構成を変えず、TCP ダウンロード負荷中の Ping を複数回計測する共有サービス。
/// 設定の切り替えと復元は、Auto-Tuning / RSC それぞれのベンチマークサービスが担当する。
/// </summary>
public interface ILoadedPingMeasurementService
{
    Task<LoadedPingMeasurementResult> MeasureAsync(
        int testSizeBytes,
        int sampleCount,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
