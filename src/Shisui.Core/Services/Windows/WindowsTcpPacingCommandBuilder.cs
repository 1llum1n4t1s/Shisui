namespace Shisui.Core.Services.Windows;

/// <summary>PC 全体の TCP 送信ペーシングを有効化する。</summary>
public static class WindowsTcpPacingCommandBuilder
{
    public const string FileName = "powershell";

    public static bool IsVerifiedOutput(string output)
    {
        var line = output.EndsWith("\r\n", StringComparison.Ordinal) ? output[..^2]
            : output.EndsWith('\n') ? output[..^1] : output;
        return line == "TCPPACING=ALWAYS";
    }

    public static string BuildEnableArguments()
    {
        var script = "$ErrorActionPreference='Stop';" +
            "$netsh=[IO.Path]::Combine([Environment]::SystemDirectory,'netsh.exe');" +
            "$written=@(& $netsh int tcp set global pacingprofile=always 2>&1);" +
            "if($LASTEXITCODE -ne 0){throw ('TCP pacing write failed: '+($written -join [Environment]::NewLine))};" +
            // dump の固定構文だけを読み、ローカライズされた show global ラベルへ依存しない。
            // これは保存された構成の確認であり、接続ごとの実際の送信挙動は保証しない。
            "$dump=@(& $netsh int tcp dump 2>&1);" +
            "if($LASTEXITCODE -ne 0){throw 'TCP pacing dump failed'};" +
            "$values=@(foreach($line in $dump){if([string]$line -match '^\\s*set\\s+global\\s+([^\\r\\n]*)$'){" +
            "$globalLine=$Matches[1];foreach($token in ($globalLine -split '\\s+')){" +
            "if($token -match '^pacingprofile=(.*)$'){$Matches[1]}}}});" +
            "if($values.Count -ne 1 -or $values[0] -cne 'always'){throw 'Stored TCP pacing profile is not uniquely always'};" +
            "'TCPPACING=ALWAYS'";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }
}
