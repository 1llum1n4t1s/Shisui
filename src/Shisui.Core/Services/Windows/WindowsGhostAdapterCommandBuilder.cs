namespace Shisui.Core.Services.Windows;

/// <summary>
/// pnputil によるネットワーククラス切断済みデバイスの列挙・削除コマンドを組み立てる純粋関数群。
/// /class Net は公式ドキュメント (PnPUtil Command Syntax) 記載のネットワークアダプタクラス名。
/// 一覧は /format xml で取得する (既定のテキスト出力は Windows の表示言語でラベルが変わるため、
/// 言語非依存にパースできる XML 出力を使う方針)。
/// </summary>
public static class WindowsGhostAdapterCommandBuilder
{
    public const string FileName = "pnputil";

    public const string ListArguments = "/enum-devices /disconnected /class Net /format xml";

    public static string BuildRemove(string instanceId) => $"/remove-device \"{instanceId}\"";
}
