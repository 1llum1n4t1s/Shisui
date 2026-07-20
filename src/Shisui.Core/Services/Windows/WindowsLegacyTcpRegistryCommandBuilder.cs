namespace Shisui.Core.Services.Windows;

/// <summary>
/// 古い「ゲーム向け TCP 高速化」手順でネットワークインターフェースごとに追加される ACK/Nagle 関連の
/// レジストリ値だけを削除し、値が存在しない Windows 既定状態へ戻す PowerShell コマンド。
/// キーや他の値は削除せず、削除件数をロケール非依存の <c>REMOVED=N</c> で出力する。
/// </summary>
public static class WindowsLegacyTcpRegistryCommandBuilder
{
    public const string FileName = "powershell";

    public const string Arguments =
        "-NoProfile -NonInteractive -Command \"" +
        "$root='HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces';" +
        "$names=@('TcpAckFrequency','TCPNoDelay','TcpDelAckTicks');" +
        "$removed=0;" +
        "Get-ChildItem -LiteralPath $root -ErrorAction Stop|ForEach-Object{" +
        "$key=$_;" +
        "$properties=Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction Stop;" +
        "foreach($name in $names){" +
        "if($properties.PSObject.Properties.Name -contains $name){" +
        "Remove-ItemProperty -LiteralPath $key.PSPath -Name $name -Force -ErrorAction Stop;" +
        "$removed++" +
        "}}};" +
        "'REMOVED='+$removed" +
        "\"";
}
