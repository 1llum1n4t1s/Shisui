using System.Globalization;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="WindowsPingCommandBuilder"/> が出力する STATUS=/RTT= 行をパースする純粋関数
/// (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsPingResultParser
{
    public static PingResult Parse(string stdout, string host, int sent)
    {
        var responseTimes = new List<double>();
        var jitterDifferences = new List<double>();
        double? previousSuccessfulRtt = null;
        int? pendingStatus = null;
        var parsedSamples = 0;

        void CompleteSample(string? rttText)
        {
            if (parsedSamples >= Math.Max(0, sent))
            {
                pendingStatus = null;
                return;
            }

            var rtt = 0.0;
            var isValidReply = pendingStatus == 0 &&
                               double.TryParse(
                                   rttText,
                                   NumberStyles.Float,
                                   CultureInfo.InvariantCulture,
                                   out rtt) &&
                               double.IsFinite(rtt) &&
                               rtt >= 0;

            if (isValidReply)
            {
                responseTimes.Add(rtt);
                if (previousSuccessfulRtt is { } previous)
                {
                    jitterDifferences.Add(Math.Abs(rtt - previous));
                }

                previousSuccessfulRtt = rtt;
            }
            else
            {
                // 失敗したプローブをまたいで前後の成功 RTT を比較しない。
                previousSuccessfulRtt = null;
            }

            parsedSamples++;
            pendingStatus = null;
        }

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim().ToUpperInvariant();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "STATUS":
                    if (pendingStatus is not null)
                    {
                        // RTT 行が欠けた STATUS も失敗した 1 プローブとして扱う。
                        CompleteSample(null);
                    }

                    pendingStatus = int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var status)
                        ? status
                        : -1;
                    break;
                case "RTT":
                    if (pendingStatus is not null)
                    {
                        CompleteSample(value);
                    }
                    break;
            }
        }

        if (pendingStatus is not null)
        {
            CompleteSample(null);
        }

        var received = responseTimes.Count;
        var average = received > 0 ? responseTimes.Average() : (double?)null;
        var sortedResponseTimes = responseTimes.Order().ToArray();
        var p95Rank = (int)Math.Ceiling(sortedResponseTimes.Length * 0.95);

        return new PingResult(received > 0, host, sent, received, average, stdout)
        {
            MinimumRoundtripMs = received > 0 ? sortedResponseTimes[0] : null,
            MaximumRoundtripMs = received > 0 ? sortedResponseTimes[^1] : null,
            P95RoundtripMs = received > 0 ? sortedResponseTimes[p95Rank - 1] : null,
            JitterMs = jitterDifferences.Count > 0 ? jitterDifferences.Average() : null,
        };
    }
}
