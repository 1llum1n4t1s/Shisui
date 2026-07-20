namespace Shisui.Core.Models;

/// <summary>RSC 有効または無効の一方について計測した、TCP受信負荷中Pingの集計結果。</summary>
public sealed record RscBenchmarkResult(
    bool Enabled,
    bool Success,
    double? AveragePingMs,
    double? MinPingMs,
    double? MaxPingMs,
    int SampleCount,
    string? ErrorMessage);

/// <summary>RSC A/B計測中の進捗通知。</summary>
public sealed record RscBenchmarkProgress(bool Enabled, int CompletedCount, int TotalCount);
