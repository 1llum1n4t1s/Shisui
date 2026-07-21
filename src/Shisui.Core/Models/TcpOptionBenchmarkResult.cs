using Shisui.Core.Interfaces;

namespace Shisui.Core.Models;

public enum TcpOptionBenchmarkMetric
{
    LoadedPing,
    DownloadSpeed,
}

/// <summary>TCPグローバルオプションの有効・無効A/B計測結果。</summary>
public sealed record TcpOptionBenchmarkResult(
    TcpGlobalOption Option,
    bool Enabled,
    TcpOptionBenchmarkMetric Metric,
    bool Success,
    double? AverageValue,
    double? MinValue,
    double? MaxValue,
    int SampleCount,
    string? ErrorMessage);

public sealed record TcpOptionBenchmarkProgress(
    TcpGlobalOption Option,
    bool Enabled,
    int CompletedCount,
    int TotalCount);
