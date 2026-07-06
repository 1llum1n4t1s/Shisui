namespace Shisui.Core.Services.Windows;

/// <summary>
/// トレースルートの経路 (ホップ IP の並び) を取得する PowerShell コマンド文字列を組み立てる純粋関数群。
/// <c>tracert.exe</c> の生テキストはホップごとの往復時間こそ持つが、見出し行や「要求がタイムアウトしました」
/// 等がロケールで変わるため、経路そのものは <c>Test-NetConnection -TraceRoute</c> の
/// TraceRoute プロパティ (IP アドレスの配列、英語固定) から取得する。各ホップの往復時間はこの経路取得の
/// 後に <see cref="WindowsPingCommandBuilder"/> で個別に ping して求める (Service 層の責務)。
/// </summary>
public static class WindowsTraceRouteCommandBuilder
{
    public const string FileName = "powershell";

    public static string BuildArguments(string host, int maxHops)
    {
        // WindowsPingCommandBuilder と同じ理由 (外側の -Command "..." を host 内の生の " で破られる
        // コマンドインジェクション経路、2026-07-06 /rere レビューで発見) で明示的に拒否する。
        if (host.Contains('"'))
        {
            throw new ArgumentException("ホスト名にダブルクオート (\") を含めることはできません。", nameof(host));
        }

        var safeHost = host.Replace("'", "''");
        return "-NoProfile -NonInteractive -Command \"" +
               $"$r=Test-NetConnection -ComputerName '{safeHost}' -TraceRoute -Hops {maxHops} " +
               "-WarningAction SilentlyContinue -ErrorAction SilentlyContinue;" +
               "if($r -and $r.TraceRoute){$r.TraceRoute|%{'HOP='+$_}}" +
               "\"";
    }
}
