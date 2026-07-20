namespace Shisui.Core.Services.Windows;

/// <summary>
/// ベンチマークの Ping 実測値を集計する純粋関数群 (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsAutoTuningBenchmarkMath
{
    /// <summary>複数回の Ping 値 (ms) を平均・最小・最大に集計する。1件も無ければ null。</summary>
    public static (double Average, double Min, double Max)? SummarizePingMilliseconds(IReadOnlyList<double> pingMilliseconds)
    {
        if (pingMilliseconds.Count == 0)
        {
            return null;
        }

        return (pingMilliseconds.Average(), pingMilliseconds.Min(), pingMilliseconds.Max());
    }
}
