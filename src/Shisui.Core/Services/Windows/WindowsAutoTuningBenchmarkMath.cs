namespace Shisui.Core.Services.Windows;

/// <summary>
/// ベンチマークの実測値を集計する純粋関数群 (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsAutoTuningBenchmarkMath
{
    /// <summary>受信バイト数と経過時間から decimal Mbps を計算する。計算不能な値なら null。</summary>
    public static double? CalculateMegabitsPerSecond(int receivedBytes, TimeSpan elapsed)
    {
        if (receivedBytes <= 0 || elapsed <= TimeSpan.Zero)
        {
            return null;
        }

        return receivedBytes * 8d / elapsed.TotalSeconds / 1_000_000d;
    }

    /// <summary>複数回の Ping 値 (ms) を平均・最小・最大に集計する。1件も無ければ null。</summary>
    public static (double Average, double Min, double Max)? SummarizePingMilliseconds(IReadOnlyList<double> pingMilliseconds)
    {
        if (pingMilliseconds.Count == 0)
        {
            return null;
        }

        return (pingMilliseconds.Average(), pingMilliseconds.Min(), pingMilliseconds.Max());
    }

    /// <summary>複数回のダウンロード速度 (Mbps) を平均・最小・最大に集計する。1件も無ければ null。</summary>
    public static (double Average, double Min, double Max)? SummarizeMegabitsPerSecond(IReadOnlyList<double> speedsMbps)
    {
        if (speedsMbps.Count == 0)
        {
            return null;
        }

        return (speedsMbps.Average(), speedsMbps.Min(), speedsMbps.Max());
    }
}
