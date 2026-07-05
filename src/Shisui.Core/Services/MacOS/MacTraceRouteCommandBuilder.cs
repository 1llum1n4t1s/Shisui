namespace Shisui.Core.Services.MacOS;

/// <summary>
/// BSD traceroute (macOS 標準) のコマンド文字列を組み立てる純粋関数群 (プロセス起動は行わない、
/// ユニットテスト対象)。-n で逆引きを止めて出力を単純化・高速化し、-q 1 でホップごとの
/// プローブ回数を 1 に絞ってパースしやすくする。
/// </summary>
public static class MacTraceRouteCommandBuilder
{
    public const string FileName = "traceroute";

    public static string BuildArguments(string host, int maxHops) =>
        $"-n -q 1 -m {maxHops} {MacShellQuote.Quote(host)}";
}
