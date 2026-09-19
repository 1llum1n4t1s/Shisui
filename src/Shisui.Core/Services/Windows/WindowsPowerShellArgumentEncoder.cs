using System.Text;

namespace Shisui.Core.Services.Windows;

/// <summary>PowerShell スクリプトを外側の引用符コンテキストから分離して渡す。</summary>
internal static class WindowsPowerShellArgumentEncoder
{
    public static string Encode(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var nonInteractiveScript = "$ProgressPreference='SilentlyContinue';" + script;
        return $"-NoProfile -NonInteractive -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(nonInteractiveScript))}";
    }
}
