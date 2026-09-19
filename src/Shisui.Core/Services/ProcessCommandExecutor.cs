using System.ComponentModel;
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

        string executablePath;
        try
        {
            executablePath = OperatingSystem.IsWindows()
                ? ResolveWindowsExecutablePath(fileName, Environment.SystemDirectory)
                : fileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new CommandExecutionResult(false, commandLine, -1, string.Empty, ex.Message);
        }

        var psi = new ProcessStartInfo(executablePath)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            // 子プロセスが相対パスを解決する場合にも、ユーザー書き込み可能な作業ディレクトリを参照させない。
            psi.WorkingDirectory = Environment.SystemDirectory;
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();

            // 生バイトで受け取ってから自前でデコードする。netsh / ipconfig 等の出力は環境によって
            // UTF-8 だったり OEM コードページ (日本語 = CP932) だったりするため、StandardOutputEncoding を
            // 固定するとどちらかの環境で文字化けする (GUI アプリは Console.OutputEncoding が OEM に解決され、
            // netsh が UTF-8 を吐くマシンだと化ける)。両ストリームを並行して読み、片方のバッファが詰まる
            // デッドロックを避ける。
            using var stdoutBuffer = new MemoryStream();
            using var stderrBuffer = new MemoryStream();
            await CopyOutputAsync(process, stdoutBuffer, stderrBuffer, ct);
            await process.WaitForExitAsync(ct);

            return new CommandExecutionResult(
                process.ExitCode == 0,
                commandLine,
                process.ExitCode,
                DecodeConsoleOutput(stdoutBuffer.ToArray()).TrimEnd(),
                DecodeConsoleOutput(stderrBuffer.ToArray()).TrimEnd());
        }
        catch (OperationCanceledException)
        {
            await TerminateProcessAsync(process);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TerminateProcessAsync(process);
            return new CommandExecutionResult(false, commandLine, -1, string.Empty, ex.Message);
        }
    }

    /// <summary>Windows の特権子プロセスは、検索順に依存しない信頼済みシステムパスへ限定する。</summary>
    internal static string ResolveWindowsExecutablePath(string fileName, string systemDirectory)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("実行ファイル名が空です。", nameof(fileName));
        }

        if (Path.IsPathFullyQualified(fileName))
        {
            return Path.GetFullPath(fileName);
        }

        if (string.IsNullOrWhiteSpace(systemDirectory) || !Path.IsPathFullyQualified(systemDirectory))
        {
            throw new ArgumentException("Windows システムディレクトリの絶対パスが必要です。", nameof(systemDirectory));
        }

        var commandName = Path.GetFileNameWithoutExtension(fileName);
        if (!string.Equals(fileName, commandName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, commandName + ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"相対パスの外部コマンドは実行できません: {fileName}");
        }

        return commandName.ToLowerInvariant() switch
        {
            "powershell" => Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            "netsh" or "ipconfig" or "pnputil" or "nbtstat" or "route" or "netcfg" =>
                Path.Combine(systemDirectory, commandName.ToLowerInvariant() + ".exe"),
            _ => throw new InvalidOperationException($"許可されていない Windows 外部コマンドです: {fileName}"),
        };
    }

    /// <summary>テストで読み取り失敗を注入できるよう、プロセス開始後の出力取得だけを分離する。</summary>
    protected virtual async Task CopyOutputAsync(
        Process process,
        MemoryStream stdoutBuffer,
        MemoryStream stderrBuffer,
        CancellationToken ct)
    {
        var readOut = process.StandardOutput.BaseStream.CopyToAsync(stdoutBuffer, ct);
        var readErr = process.StandardError.BaseStream.CopyToAsync(stderrBuffer, ct);
        await Task.WhenAll(readOut, readErr);
    }

    /// <summary>待機のキャンセル後も管理者権限の子プロセスを残さないよう、プロセスツリーを終了する。</summary>
    internal static async Task TerminateProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 既に終了した、または終了要求を受け付けないプロセスでも、元のキャンセルはそのまま通知する。
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
