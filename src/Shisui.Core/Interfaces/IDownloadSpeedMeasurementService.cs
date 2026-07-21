using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>1つの TCP 構成について、複数回のダウンロード速度を計測する。</summary>
public interface IDownloadSpeedMeasurementService
{
    Task<DownloadSpeedMeasurementResult> MeasureAsync(
        int testSizeBytes,
        int sampleCount,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
