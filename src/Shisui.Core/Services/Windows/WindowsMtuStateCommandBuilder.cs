namespace Shisui.Core.Services.Windows;

/// <summary>
/// 指定アダプタの現在の IPv4 MTU を読み取る PowerShell コマンド文字列を組み立てる純粋関数群。
/// <c>Get-NetIPInterface</c> の NlMtu は数値プロパティなのでロケール非依存。
/// </summary>
public static class WindowsMtuStateCommandBuilder
{
    public const string FileName = "powershell";

    public static string BuildArguments(string adapterName)
    {
        var safeName = adapterName.Replace("'", "''");
        return "-NoProfile -NonInteractive -Command \"" +
               $"$i=Get-NetIPInterface -InterfaceAlias '{safeName}' -AddressFamily IPv4 -ErrorAction SilentlyContinue;" +
               "if($i){'MTU='+$i.NlMtu}" +
               "\"";
    }
}
