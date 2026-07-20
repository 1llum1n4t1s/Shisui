namespace Shisui.Core.Models;

/// <summary>1つの TCP 構成について計測した、TCP受信負荷中Pingの集計結果。</summary>
public sealed record LoadedPingMeasurementResult(
    bool Success,
    double? AveragePingMs,
    double? MinPingMs,
    double? MaxPingMs,
    int SampleCount,
    string? ErrorMessage);
