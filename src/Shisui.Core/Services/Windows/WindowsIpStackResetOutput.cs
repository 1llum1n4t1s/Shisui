using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>netsh の終了コードが 1 でも、既知の全項目成功表示と一致する場合だけ完了と判定する。</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsIpStackResetOutput
{
    private const string CategoryToken = "%1!s!";

    public static bool IsSuccessfulReset(CommandExecutionResult result)
    {
        if (!OperatingSystem.IsWindows() || result.Success || result.ExitCode != 1 ||
            result.RequiresOperationAbort || result.StandardError.Length != 0)
            return false;

        nint module = 0;
        try
        {
            // データとして読み込み、DLL の初期化やネットワーク操作は実行しない。
            module = LoadLibraryEx(Path.Combine(Environment.SystemDirectory, "netiohlp.dll"), 0, 0x22);
            if (module == 0)
                return false;

            var successTemplate = LoadResourceString(module, 0xc3b4);
            var trailer = LoadResourceString(module, 0xf78);
            if (successTemplate.Length == 0 || trailer.Length == 0 ||
                !successTemplate.Contains(CategoryToken, StringComparison.Ordinal) ||
                successTemplate.IndexOf(CategoryToken, StringComparison.Ordinal) !=
                    successTemplate.LastIndexOf(CategoryToken, StringComparison.Ordinal) ||
                successTemplate.Replace(CategoryToken, string.Empty, StringComparison.Ordinal).Contains('%') ||
                trailer.Contains('%'))
                return false;

            var expected = new StringBuilder();
            var count = 0;
            for (uint category = 1; category <= 36; category++)
            {
                if (category is 4 or 25)
                    continue;

                var name = LoadResourceString(module, category + 0x238c);
                // 解析・実測済みの 19 有名項目と 15 空名項目に限定し、未知の構成は失敗のまま残す。
                if ((category <= 20) == (name.Length == 0))
                    return false;

                expected.Append(successTemplate.Replace(CategoryToken, name, StringComparison.Ordinal));
                count++;
            }
            expected.Append(trailer);
            return count == 34 && string.Equals(Normalize(result.StandardOutput), Normalize(expected.ToString()),
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
                                   BadImageFormatException or System.Security.SecurityException)
        {
            // 資源を読めない環境では通常の終了コード判定へ戻し、後続の再設定を妨げない。
            return false;
        }
        finally
        {
            if (module != 0)
                FreeLibrary(module);
        }
    }

    private static string LoadResourceString(nint module, uint id)
    {
        // 長さ 0 を指定し、資源内の読み取り専用文字列を参照する。固定長バッファで切り詰めない。
        var length = LoadString(module, id, out var text, 0);
        return length > 0 && text != 0 ? Marshal.PtrToStringUni(text, length) : string.Empty;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryEx(string path, nint file, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "LoadStringW")]
    private static partial int LoadString(nint module, uint id, out nint text, int maximumLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "FreeLibrary")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);
}
