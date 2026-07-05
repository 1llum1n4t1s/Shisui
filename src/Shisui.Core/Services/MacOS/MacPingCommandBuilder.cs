namespace Shisui.Core.Services.MacOS;

/// <summary>
/// BSD ping (macOS 標準) のコマンド文字列を組み立てる純粋関数群 (プロセス起動は行わない、
/// ユニットテスト対象)。macOS の ping は -c を付けないと停止せず流れ続けるため必須。
/// </summary>
public static class MacPingCommandBuilder
{
    public const string FileName = "ping";

    public static string BuildArguments(string host, int count) =>
        $"-c {count} {MacShellQuote.Quote(host)}";
}
