using System.Text;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// PowerShell スクリプトへ値を埋め込むための単一引用符リテラル生成を一か所に集約する。
/// <para>
/// PowerShell のトークナイザーは ASCII の <c>'</c> に加えて U+2018 / U+2019 / U+201A / U+201B も
/// 単一引用符として扱う (Windows PowerShell 5.1 / PowerShell 7 とも)。<c>'</c> だけを二重化すると、
/// 値中の <c>’</c> などでリテラルが閉じ、後続テキストがコードとして評価される。
/// ここでは各引用符文字を「その文字自身」で二重化し、値全体を一つのリテラルに保つ。
/// </para>
/// <para>
/// 単一引用符リテラル内では変数展開・サブ式評価が起きないため、引用符以外の文字は加工しない。
/// 外側の <c>-Command "..."</c> を破る ASCII の <c>"</c> などは、呼び出し側の既存検証で拒否する。
/// </para>
/// </summary>
internal static class WindowsPowerShellLiteral
{
    /// <summary>PowerShell が単一引用符として解釈する全文字。</summary>
    private static readonly char[] SingleQuoteCharacters = ['\'', '‘', '’', '‚', '‛'];

    /// <summary>
    /// 単一引用符リテラルの内側に置く値をエスケープする。囲みの引用符は付けない。
    /// 引用符を含まない値は同じインスタンスをそのまま返す (生成文字列は従来と完全一致)。
    /// </summary>
    internal static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOfAny(SingleQuoteCharacters) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            builder.Append(c);
            if (Array.IndexOf(SingleQuoteCharacters, c) >= 0)
            {
                // 同じ文字で二重化すると、PowerShell はリテラル内の引用符 1 文字として扱う。
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>値を <c>'...'</c> で囲んだ PowerShell の単一引用符リテラルにする。</summary>
    internal static string Quote(string value) => "'" + Escape(value) + "'";
}
