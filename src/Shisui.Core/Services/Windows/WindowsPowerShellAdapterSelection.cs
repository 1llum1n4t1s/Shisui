namespace Shisui.Core.Services.Windows;

/// <summary>PowerShell上でアダプター名をワイルドカードとして解釈せず、一意に解決する。</summary>
internal static class WindowsPowerShellAdapterSelection
{
    internal static string BuildExactLookup(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            throw new ArgumentException("アダプター名が必要です", nameof(adapterName));
        }

        var name = adapterName.Replace("'", "''", StringComparison.Ordinal);
        return "$a=@(Get-NetAdapter -Name '*' -IncludeHidden -ErrorAction Stop | " +
               $"Where-Object {{[string]::Equals([string]$_.Name,'{name}',[StringComparison]::OrdinalIgnoreCase)}});" +
               "if($a.Count -ne 1){throw 'Adapter resolution was not unique'};";
    }
}
