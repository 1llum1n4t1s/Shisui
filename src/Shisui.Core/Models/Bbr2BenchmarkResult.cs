namespace Shisui.Core.Models;

/// <summary>BBR2有効・既定の一方について計測した、TCP受信負荷中Pingの集計結果。</summary>
public sealed record Bbr2BenchmarkResult(
    bool Enabled,
    bool Success,
    double? AveragePingMs,
    double? MinPingMs,
    double? MaxPingMs,
    int SampleCount,
    string? ErrorMessage);

public sealed record Bbr2BenchmarkProgress(bool Enabled, int CompletedCount, int TotalCount);
