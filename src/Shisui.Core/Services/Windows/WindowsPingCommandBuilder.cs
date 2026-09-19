namespace Shisui.Core.Services.Windows;

/// <summary>
/// ping の PowerShell コマンド文字列を組み立てる純粋関数群。<c>ping.exe</c> の生テキストではなく
/// <see cref="System.Net.NetworkInformation.Ping"/> を使い、成功・失敗にかかわらず各プローブの
/// STATUS/RTT を必ず出力する。出力値は数値だけなのでロケールに依存しない。
/// </summary>
public static class WindowsPingCommandBuilder
{
    public const string FileName = "powershell";
    public const int MinimumCount = 1;
    public const int MaximumCount = 100;

    private const int TimeoutMilliseconds = 1_000;
    private const int IntervalMilliseconds = 1_000;

    public static string BuildArguments(string host, int count)
    {
        // host に生の " が含まれると、外側の -Command "..." の引用符コンテキストがそこで終端し、
        // 後続テキストが別コマンドとして注入される (2026-07-06 /rere レビューで発見)。シングルクオートの
        // 二重化だけでは、この "外側のダブルクオートを破る" 経路は防げないため、ここで明示的に拒否する。
        if (host.Contains('"'))
        {
            throw new ArgumentException("ホスト名にダブルクオート (\") を含めることはできません。", nameof(host));
        }

        if (count is < MinimumCount or > MaximumCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                $"ping 回数は {MinimumCount}～{MaximumCount} の範囲で指定してください。");
        }

        // PowerShell のシングルクォート文字列リテラルはこの文字だけを二重化すればコマンド注入されない
        // (変数展開・サブ式評価が一切ない純粋なリテラルのため)。
        var safeHost = host.Replace("'", "''");
        return "-NoProfile -NonInteractive -Command \"" +
               "$utf8=[System.Text.UTF8Encoding]::new($false);[Console]::OutputEncoding=$utf8;$OutputEncoding=$utf8;" +
               "$culture=[System.Globalization.CultureInfo]::InvariantCulture;" +
               "$ping=[System.Net.NetworkInformation.Ping]::new();" +
               $"try{{for($i=0;$i -lt {count};$i++){{" +
               "$probe=[System.Diagnostics.Stopwatch]::StartNew();" +
               $"try{{$reply=$ping.Send('{safeHost}',{TimeoutMilliseconds});" +
               "'STATUS='+([int]$reply.Status).ToString($culture);" +
               "if($reply.Status -eq [System.Net.NetworkInformation.IPStatus]::Success){" +
               "'RTT='+$reply.RoundtripTime.ToString($culture)}else{'RTT='}}" +
               "catch{'STATUS=-1';'RTT='};" +
               $"$delay={IntervalMilliseconds}-[int]$probe.ElapsedMilliseconds;" +
               $"if($i -lt {count - 1} -and $delay -gt 0){{Start-Sleep -Milliseconds $delay}}" +
               "}}finally{$ping.Dispose()}" +
               "\"";
    }
}
