namespace Shisui.Core.Models;

/// <summary>
/// 簡易疎通テスト (ping) の集計結果。
/// </summary>
public sealed record PingResult(
    bool Success,
    string Host,
    int Sent,
    int Received,
    double? AverageRoundtripMs,
    string RawOutput)
{
    public double? MinimumRoundtripMs { get; init; }

    public double? MaximumRoundtripMs { get; init; }

    public double? P95RoundtripMs { get; init; }

    /// <summary>
    /// 連続して成功したプローブ間の RTT 差の絶対値を平均したジッター。
    /// </summary>
    public double? JitterMs { get; init; }

    public double? LossPercent => Sent == 0 ? null : (Sent - Received) * 100.0 / Sent;

    public static PingResult Failed(string host, string rawOutput) =>
        new(false, host, 0, 0, null, rawOutput);
}
