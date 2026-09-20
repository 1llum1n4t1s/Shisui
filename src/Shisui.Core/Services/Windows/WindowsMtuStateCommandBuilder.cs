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
        var script = "$ErrorActionPreference='Stop';" +
                     WindowsPowerShellAdapterSelection.BuildExactLookup(adapterName) +
                     "$i=@(Get-NetIPInterface -InterfaceIndex ([uint32]$a[0].ifIndex) -AddressFamily IPv4 -ErrorAction Stop);" +
                     "if($i.Count -ne 1){throw 'IPv4 interface resolution was not unique'};" +
                     "'MTU='+$i[0].NlMtu";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }
}
