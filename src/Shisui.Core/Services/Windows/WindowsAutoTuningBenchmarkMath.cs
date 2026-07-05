namespace Shisui.Core.Services.Windows;

/// <summary>
/// ベンチマークの実測値からスループットを計算する純粋関数群 (プロセス起動・OS 呼び出しを行わない、
/// ユニットテスト対象)。
/// </summary>
public static class WindowsAutoTuningBenchmarkMath
{
    public static double? ComputeThroughputMbps(long bytesReceived, TimeSpan elapsed)
    {
        if (bytesReceived <= 0 || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return bytesReceived * 8.0 / elapsed.TotalSeconds / 1_000_000.0;
    }

    /// <summary>複数回のスループット計測値を平均・最小・最大に集計する。1件も無ければ null。</summary>
    public static (double Average, double Min, double Max)? Summarize(IReadOnlyList<double> throughputsMbps)
    {
        if (throughputsMbps.Count == 0)
        {
            return null;
        }

        return (throughputsMbps.Average(), throughputsMbps.Min(), throughputsMbps.Max());
    }
}
