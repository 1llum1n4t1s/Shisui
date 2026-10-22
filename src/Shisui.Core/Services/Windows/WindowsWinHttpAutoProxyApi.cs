using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsWinHttpAutoProxyApi : IWinHttpAutoProxyApi
{
    // STATE / ALL / NOTIFY は含めず、サービス側の PAD とスクリプトキャッシュだけを対象にする。
    internal const uint CacheOnlyFlags = 0x00020000 | 0x00000004 | 0x00000008;
    private const uint ErrorInvalidParameter = 87;
    private const uint ErrorInvalidHandle = 6;

    public uint ResetAutoProxy(uint flags)
    {
        // 呼び出し側の誤りでもプロキシ状態リセットへ拡張されないよう、完全一致のみ許可する。
        if (flags != CacheOnlyFlags)
            return ErrorInvalidParameter;

        // NO_PROXY の専用セッション。リクエスト送信や保存済み設定の書き換えは行わない。
        var session = WinHttpOpen("Shisui", 1, null, null, 0);
        if (session == 0 || session == -1)
            return LastErrorOr(ErrorInvalidHandle);

        uint result;
        try
        {
            result = WinHttpResetAutoProxy(session, flags);
        }
        finally
        {
            // Reset が例外を投げても、自分が開いたセッションは閉じる。
            if (!WinHttpCloseHandle(session))
                throw new System.ComponentModel.Win32Exception(unchecked((int)LastErrorOr(ErrorInvalidHandle)),
                    "WinHTTP の専用セッションを閉じられませんでした。");
        }

        return result;
    }

    private static uint LastErrorOr(uint fallback)
    {
        var error = unchecked((uint)Marshal.GetLastPInvokeError());
        return error == 0 ? fallback : error;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winhttp.dll", EntryPoint = "WinHttpOpen", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint WinHttpOpen(string agent, uint accessType, string? proxy, string? bypass, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winhttp.dll", EntryPoint = "WinHttpResetAutoProxy")]
    private static partial uint WinHttpResetAutoProxy(nint session, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("winhttp.dll", EntryPoint = "WinHttpCloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WinHttpCloseHandle(nint session);
}
