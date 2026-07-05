namespace Shisui.Core.Services.Windows;

/// <summary>
/// 現在の DoH 登録状況を「ロケール非依存で」読み取るための PowerShell コマンド。
/// Get-DnsClientDohServerAddress は英語固定のプロパティ名 (ServerAddress / AutoUpgrade 等) を
/// 持つコマンドレットなので、これを KEY=VALUE 形式で出力させれば OS 表示言語に関わらずパースできる
/// (WindowsTcpStateCommandBuilder と同じ設計)。
/// </summary>
public static class WindowsDohStateCommandBuilder
{
    public const string FileName = "powershell";

    public static string BuildArguments(IReadOnlyList<string> serverAddresses)
    {
        var addressList = string.Join(",", serverAddresses.Select(a => $"'{a}'"));
        return "-NoProfile -NonInteractive -Command \"" +
               $"Get-DnsClientDohServerAddress -ServerAddress {addressList} -ErrorAction SilentlyContinue | " +
               "%{'SERVER='+$_.ServerAddress;'AUTOUPGRADE='+$_.AutoUpgrade}" +
               "\"";
    }
}
