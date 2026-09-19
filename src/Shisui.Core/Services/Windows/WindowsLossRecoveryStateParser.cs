namespace Shisui.Core.Services.Windows;

/// <summary>
/// PowerShell に公開されていない RACK/TLP の限定的な読み戻し。
/// 確認済みの日英ラベルのみを受理し、未知の言語・欠落・重複は変更不要と判定しない。
/// </summary>
internal static class WindowsLossRecoveryStateParser
{
    public static bool AreBothEnabled(string output)
    {
        var rack = new List<string>();
        var tlp = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2) continue;
            var label = parts[0].Trim();
            var value = parts[1].Trim();
            if (label is "Enable RACK" or "RACK の有効化") rack.Add(value);
            if (label is "Enable Tail Loss Probe" or "Tail Loss Probe の有効化") tlp.Add(value);
        }

        return rack.Count == 1 && tlp.Count == 1 &&
               rack[0].Equals("enabled", StringComparison.OrdinalIgnoreCase) &&
               tlp[0].Equals("enabled", StringComparison.OrdinalIgnoreCase);
    }
}
