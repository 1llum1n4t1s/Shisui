namespace Shisui.Core.Services.MacOS;

/// <summary>
/// sh (do shell script が実際に解釈するシェル) の二重引用符コンテキスト向けクォート。
/// </summary>
internal static class MacShellQuote
{
    public static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\"";
}
