namespace Shisui.Core.Models;

/// <summary>1つの TCP 構成について計測したダウンロード速度の集計結果。</summary>
public sealed record DownloadSpeedMeasurementResult(
    bool Success,
    double? AverageMbps,
    double? MinMbps,
    double? MaxMbps,
    int SampleCount,
    string? ErrorMessage);
