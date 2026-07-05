namespace Shisui.Core.Services.Windows;

/// <summary>
/// ping の PowerShell コマンド文字列を組み立てる純粋関数群。<c>ping.exe</c> の生テキストではなく
/// <c>Test-Connection</c> (Win32_PingStatus ベース) を使う。StatusCode (0=成功) / ResponseTime (ms) が
/// 英語固定の数値プロパティなので、WindowsTcpStateCommandBuilder と同じ設計でロケール非依存になる。
/// </summary>
public static class WindowsPingCommandBuilder
{
    public const string FileName = "powershell";

    public static string BuildArguments(string host, int count)
    {
        // PowerShell のシングルクォート文字列リテラルはこの文字だけを二重化すればコマンド注入されない
        // (変数展開・サブ式評価が一切ない純粋なリテラルのため)。
        var safeHost = host.Replace("'", "''");
        return "-NoProfile -NonInteractive -Command \"" +
               $"$r=Test-Connection -ComputerName '{safeHost}' -Count {count} -ErrorAction SilentlyContinue;" +
               "if($r){$r|%{'STATUS='+$_.StatusCode;'RTT='+$_.ResponseTime}}" +
               "\"";
    }
}
