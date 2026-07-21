using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 現在の TCP 構成を変えず、TCP ダウンロード負荷中の Ping を複数回計測するサービス。
/// RSC の設定切り替えと復元は <see cref="IRscBenchmarkService"/> が担当する。
/// </summary>
public interface ILoadedPingMeasurementService
{
    Task<LoadedPingMeasurementResult> MeasureAsync(
        int testSizeBytes,
        int sampleCount,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
