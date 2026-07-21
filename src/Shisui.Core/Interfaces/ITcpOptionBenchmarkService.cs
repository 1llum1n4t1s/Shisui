using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>復元可能なTCPグローバルオプションを有効・無効で比較するWindows専用サービス。</summary>
public interface ITcpOptionBenchmarkService
{
    Task<IReadOnlyList<TcpOptionBenchmarkResult>> RunAsync(
        TcpGlobalOption option,
        TcpOptionBenchmarkMetric metric,
        int testSizeBytes,
        IProgress<TcpOptionBenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}
