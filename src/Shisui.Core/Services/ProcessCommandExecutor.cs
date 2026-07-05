using System.Diagnostics;
using System.Globalization;
using System.Text;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// 外部プロセスをそのまま起動する既定の ICommandExecutor。
/// Windows ではアプリ自体が管理者権限で起動しているため、子プロセスもそのまま昇格状態を継承する。
/// </summary>
public class ProcessCommandExecutor : ICommandExecutor
{
    // 厳密 UTF-8 (不正バイトで例外)。CodePages プロバイダに依存しないので静的初期化順の問題も無い。
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // OEM コードページ (日本語 Windows なら CP932)。UTF-8 デコード失敗時のフォールバック用。
    private static readonly Encoding OemEncoding;

    static ProcessCommandExecutor()
    {
        // .NET Core 既定では CP932 等のレガシーコードページが未登録なので、フォールバック用に登録する。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            OemEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception)
        {
            // 想定外のコードページでも落ちないよう、全バイトを写像できる Latin1 を最終手段にする。
            OemEncoding = Encoding.Latin1;
        }
    }

    public async Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var commandLine = string.IsNullOrEmpty(arguments) ? fileName : $"{fileName} {arguments}";

        var psi = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            // 生バイトで受け取ってから自前でデコードする。netsh / ipconfig 等の出力は環境によって
            // UTF-8 だったり OEM コードページ (日本語 = CP932) だったりするため、StandardOutputEncoding を
            // 固定するとどちらかの環境で文字化けする (GUI アプリは Console.OutputEncoding が OEM に解決され、
            // netsh が UTF-8 を吐くマシンだと化ける)。両ストリームを並行して読み、片方のバッファが詰まる
            // デッドロックを避ける。
            using var stdoutBuffer = new MemoryStream();
            using var stderrBuffer = new MemoryStream();
            var readOut = process.StandardOutput.BaseStream.CopyToAsync(stdoutBuffer, ct);
            var readErr = process.StandardError.BaseStream.CopyToAsync(stderrBuffer, ct);
            await Task.WhenAll(readOut, readErr);
            await process.WaitForExitAsync(ct);

            return new CommandExecutionResult(
                process.ExitCode == 0,
                commandLine,
                process.ExitCode,
                DecodeConsoleOutput(stdoutBuffer.ToArray()).TrimEnd(),
                DecodeConsoleOutput(stderrBuffer.ToArray()).TrimEnd());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CommandExecutionResult(false, commandLine, -1, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// まず厳密 UTF-8 として解釈し、不正バイトがあれば OEM コードページにフォールバックする。
    /// CP932 の日本語バイト列が偶然 UTF-8 として妥当になることはほぼ無い (CP932 の先行/後続バイトが
    /// UTF-8 の継続バイト規則をまず満たさない) ため、この順序で環境差を安全に自動判別できる。
    /// </summary>
    internal static string DecodeConsoleOutput(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return OemEncoding.GetString(bytes);
        }
    }
}
