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
        var received = 0;
        int? pendingStatus = null;

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
                    pendingStatus = int.TryParse(value, out var status) ? status : -1;
                    break;
                case "RTT":
                    if (pendingStatus == 0 && double.TryParse(value, out var rtt))
                    {
                        received++;
                        responseTimes.Add(rtt);
                    }

                    pendingStatus = null;
                    break;
            }
        }

        var average = responseTimes.Count > 0 ? responseTimes.Average() : (double?)null;
        return new PingResult(received > 0, host, sent, received, average, stdout);
    }
}
