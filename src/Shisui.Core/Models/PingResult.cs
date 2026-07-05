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
    public static PingResult Failed(string host, string rawOutput) =>
        new(false, host, 0, 0, null, rawOutput);
}
