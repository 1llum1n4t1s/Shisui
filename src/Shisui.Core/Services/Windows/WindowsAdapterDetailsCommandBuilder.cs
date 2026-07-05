namespace Shisui.Core.Services.Windows;

/// <summary>
/// ネットワークアダプタの詳細情報 (MAC アドレス・リンク速度等) を読み取る PowerShell コマンド文字列を
/// 組み立てる純粋関数群。<c>Get-NetAdapter</c> の各プロパティは英語固定でロケールに依存しない。
/// </summary>
public static class WindowsAdapterDetailsCommandBuilder
{
    public const string FileName = "powershell";

    public static string BuildArguments(string adapterName)
    {
        var safeName = adapterName.Replace("'", "''");
        return "-NoProfile -NonInteractive -Command \"" +
               $"Get-NetAdapter -Name '{safeName}' -ErrorAction SilentlyContinue | " +
               "%{'MAC='+$_.MacAddress;'LINKSPEED='+$_.LinkSpeed;'MEDIATYPE='+$_.MediaType;'STATUS='+$_.Status}" +
               "\"";
    }
}
