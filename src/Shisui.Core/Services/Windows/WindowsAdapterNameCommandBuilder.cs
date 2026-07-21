using System.Text;

namespace Shisui.Core.Services.Windows;

public static class WindowsAdapterNameCommandBuilder
{
    public const string FileName = "powershell";

    private const string QueryScript =
        "$ProgressPreference='SilentlyContinue';$e=[Text.Encoding]::UTF8;" +
        "Get-NetAdapter -Name '*' -IncludeHidden | %{" +
        "'BEGIN';" +
        "'NAME='+[Convert]::ToBase64String($e.GetBytes([string]$_.Name));" +
        "'END'}";

    public static string QueryArguments { get; } = BuildEncodedArguments(QueryScript);

    public static string BuildRenameArguments(string currentName, string targetName)
    {
        var safeCurrentName = QuotePowerShellLiteral(currentName);
        var safeTargetName = QuotePowerShellLiteral(targetName);
        var script =
            "$ProgressPreference='SilentlyContinue';$a=Get-NetAdapter -Name '*' -IncludeHidden | " +
            $"?{{[string]::Equals($_.Name,{safeCurrentName},[StringComparison]::OrdinalIgnoreCase)}};" +
            "if(-not $a){throw 'Adapter not found'};" +
            $"$a | Rename-NetAdapter -NewName {safeTargetName} " +
            "-PassThru -Confirm:$false -ErrorAction Stop | %{'RENAMED='+$_.Name}";
        return BuildEncodedArguments(script);
    }

    private static string QuotePowerShellLiteral(string value) => $"'{value.Replace("'", "''")}'";

    private static string BuildEncodedArguments(string script) =>
        $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}";
}
