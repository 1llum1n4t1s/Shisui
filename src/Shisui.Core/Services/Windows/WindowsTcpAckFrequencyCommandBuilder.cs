using System.Globalization;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>選択 NIC の既存キーにある TcpAckFrequency だけを操作する純粋関数群。</summary>
public static class WindowsTcpAckFrequencyCommandBuilder
{
    // 配布先に PowerShell 7 の追加導入を要求せず、Windows 標準の 5.1 で実行する。
    public const string FileName = "powershell";
    private const string Marker = "TCPACKFREQUENCY=";

    public static string BuildGetArguments(string adapterId) => BuildArguments(adapterId, null);

    public static string BuildSetArguments(string adapterId, bool enabled) => BuildArguments(adapterId, enabled);

    public static TcpAckFrequencyState ParseState(string output)
    {
        // 終端改行だけを許し、重複マーカーや付加出力は成功した読み取りにしない。
        var line = output.EndsWith("\r\n", StringComparison.Ordinal) ? output[..^2]
            : output.EndsWith('\n') ? output[..^1] : output;
        if (line == Marker + "DEFAULT")
            return TcpAckFrequencyState.Default;

        const string prefix = Marker + "DWORD:";
        if (line.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(line.AsSpan(prefix.Length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            && line[prefix.Length..] == value.ToString(CultureInfo.InvariantCulture))
            return new TcpAckFrequencyState(true, value);

        return TcpAckFrequencyState.Unknown;
    }

    private static string BuildArguments(string adapterId, bool? enabled)
    {
        var lookup = WindowsPowerShellAdapterSelection.BuildExactLookup(adapterId);
        var mutation = enabled switch
        {
            true => "$key.SetValue('TcpAckFrequency',[int]1,[Microsoft.Win32.RegistryValueKind]::DWord);$key.Flush();",
            false => "$key.DeleteValue('TcpAckFrequency',$false);$key.Flush();",
            null => string.Empty,
        };
        var expected = enabled switch
        {
            true => "if($state -cne 'DWORD:1'){throw 'TcpAckFrequency write verification failed'};",
            false => "if($state -cne 'DEFAULT'){throw 'TcpAckFrequency removal verification failed'};",
            null => string.Empty,
        };
        var writable = enabled.HasValue ? "$true" : "$false";
        var script = "$ErrorActionPreference='Stop';" + lookup +
            "$guid=[guid]$a[0].InterfaceGuid;" +
            "if($guid -eq [guid]::Empty){throw 'Adapter GUID is empty'};" +
            "$path='SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\'+$guid.ToString('B');" +
            "$base=$null;$key=$null;try{" +
            "$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64);" +
            $"$key=$base.OpenSubKey($path,{writable});" +
            "if($null -eq $key){throw 'Existing adapter registry key not found'};" + mutation +
            "$state='DEFAULT';" +
            "if($key.GetValueNames() -contains 'TcpAckFrequency'){" +
            "if($key.GetValueKind('TcpAckFrequency') -ne [Microsoft.Win32.RegistryValueKind]::DWord){throw 'TcpAckFrequency is not DWORD'};" +
            "$value=$key.GetValue('TcpAckFrequency',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);" +
            "if($value -isnot [int]){throw 'TcpAckFrequency DWORD could not be read'};" +
            "$state='DWORD:'+$value.ToString([Globalization.CultureInfo]::InvariantCulture)};" + expected +
            lookup +
            "if([guid]$a[0].InterfaceGuid -ne $guid){throw 'Adapter changed during operation'};" +
            "'TCPACKFREQUENCY='+$state" +
            "}finally{if($null -ne $key){$key.Dispose()};if($null -ne $base){$base.Dispose()}}";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }
}
