using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>Winsock SPI を使い、非表示・単独の LSP を含む全登録を読み取る。</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsWinsockCatalogInspector(IWinsockCatalogApi? api = null) : IWinsockCatalogInspector
{
    private readonly IWinsockCatalogApi _api = api ?? new NativeWinsockCatalogApi();
    private const int SocketError = -1;
    private const int WsaNoBuffers = 10055;
    private const int MaximumBufferBytes = 1024 * 1024;
    private const int MaximumAttempts = 3;
    private const int MaximumProtocolChain = 7;

    // winsock2.h の WSAPROTOCOL_INFOW はポインターを含まず、32/64 ビットとも同じ配置。
    // DWORD 5個 + GUID + DWORD = 40、CHAIN = 32、後続 int/DWORD 11個 + WCHAR 256個 = 556。
    // https://learn.microsoft.com/windows/win32/api/winsock2/ns-winsock2-wsaprotocol_infow
    private const int ProtocolInfoBytes = 628;
    private const int ProtocolChainLengthOffset = 40;

    // WSADATA は SDK の 32 ビット構造で 400、64 ビット構造で 408 バイト。
    private const int WsaDataBufferBytes = 512;
    private const ushort WinsockVersion = 0x0202;

    public Task<WinsockCatalogInspection> InspectAsync(CancellationToken ct = default) =>
        Task.Run(() => Inspect(ct), ct);

    private WinsockCatalogInspection Inspect(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (_api.Is64BitOperatingSystem && !_api.Is64BitProcess)
            {
                return Unknown("32 ビットプロセスから 64 ビット側の Winsock 登録を確認できません。");
            }

            var inspection = InspectWithWinsock(ct);
            ct.ThrowIfCancellationRequested();
            return inspection;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();
            return Unknown($"Winsock 登録の読み取りに失敗しました ({ex.GetType().Name})。");
        }
    }

    private WinsockCatalogInspection InspectWithWinsock(CancellationToken ct)
    {
        var started = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            var startupResult = _api.Startup();
            if (startupResult != 0)
            {
                return Unknown($"Winsock の読み取り準備に失敗しました (WSAStartup: {startupResult})。");
            }

            started = true;
            return InspectCatalogs(ct);
        }
        finally
        {
            if (started)
            {
                _api.Cleanup();
            }
        }
    }

    private WinsockCatalogInspection InspectCatalogs(CancellationToken ct)
    {
        var native = InspectCatalog(false, ct);
        if (!native.IsComplete || !_api.Is64BitOperatingSystem)
        {
            return native;
        }

        var catalog32 = InspectCatalog(true, ct);
        return new(native.IsComplete && catalog32.IsComplete,
            native.HasLayeredProviders || catalog32.HasLayeredProviders,
            $"{native.Detail} {catalog32.Detail}");
    }

    private WinsockCatalogInspection InspectCatalog(bool catalog32, CancellationToken ct)
    {
        var catalogName = catalog32 ? "32 ビット側" : $"{(_api.Is64BitProcess ? 64 : 32)} ビット側";
        ct.ThrowIfCancellationRequested();
        uint requiredBytes = 0;
        var count = _api.EnumProtocols(catalog32, IntPtr.Zero, ref requiredBytes, out var error);
        if (count != SocketError || error != WsaNoBuffers || !IsValidBufferSize(requiredBytes))
        {
            return Unknown($"{catalogName}: Winsock 登録の必要容量を確認できません (結果: {count}, エラー: {error})。");
        }

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var allocatedBytes = (int)requiredBytes;
            var buffer = Marshal.AllocHGlobal(allocatedBytes);
            try
            {
                var returnedBytes = requiredBytes;
                count = _api.EnumProtocols(catalog32, buffer, ref returnedBytes, out error);
                ct.ThrowIfCancellationRequested();
                if (count == SocketError && error == WsaNoBuffers)
                {
                    if (!IsValidBufferSize(returnedBytes) || returnedBytes <= requiredBytes)
                    {
                        return Unknown($"{catalogName}: Winsock 登録の容量が不正です。");
                    }

                    requiredBytes = returnedBytes;
                    continue;
                }

                // 件数を先に制限することで乗算のオーバーフローとバッファ外読み取りを防ぐ。
                if (count <= 0 || count > allocatedBytes / ProtocolInfoBytes
                    || returnedBytes > allocatedBytes || returnedBytes < (uint)(count * ProtocolInfoBytes))
                {
                    return Unknown($"{catalogName}: Winsock 登録を完全に列挙できません (結果: {count}, エラー: {error})。");
                }

                var layeredCount = 0;
                for (var index = 0; index < count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var chainLength = Marshal.ReadInt32(buffer, index * ProtocolInfoBytes + ProtocolChainLengthOffset);
                    if (chainLength < 0 || chainLength > MaximumProtocolChain)
                    {
                        return Unknown($"{catalogName}: Winsock 登録のチェーン長が不正です。");
                    }

                    // 0 は単独 LSP、1 は基底、2～7 は LSP を含むチェーン。
                    if (chainLength != 1)
                    {
                        layeredCount++;
                    }
                }

                return new(true, layeredCount > 0,
                    $"{catalogName}: {count} 件を確認、LSP または LSP チェーン {layeredCount} 件。");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return Unknown($"{catalogName}: Winsock 登録の増加が続き、列挙を完了できません。");
    }

    private static bool IsValidBufferSize(uint bytes) =>
        bytes >= ProtocolInfoBytes && bytes <= MaximumBufferBytes;

    private static WinsockCatalogInspection Unknown(string detail) => new(false, false, detail);

    private sealed partial class NativeWinsockCatalogApi : IWinsockCatalogApi
    {
        public bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;
        public bool Is64BitProcess => Environment.Is64BitProcess;

        public int Startup()
        {
            var wsaData = Marshal.AllocHGlobal(WsaDataBufferBytes);
            try
            {
                var result = WSAStartup(WinsockVersion, wsaData);
                if (result != 0 || unchecked((ushort)Marshal.ReadInt16(wsaData)) == WinsockVersion)
                {
                    return result;
                }

                Cleanup();
                return 10092; // WSAVERNOTSUPPORTED: 2.2 を使えない場合は検査しない。
            }
            finally
            {
                Marshal.FreeHGlobal(wsaData);
            }
        }

        public void Cleanup()
        {
            if (WSACleanup() != 0)
            {
                throw new InvalidOperationException("Winsock の読み取り終了処理に失敗しました。");
            }
        }

        // WSAEnumProtocols は非表示と単独 LSP を省くため使用しない。
        public int EnumProtocols(bool catalog32, nint buffer, ref uint length, out int error) =>
            catalog32
                ? WSCEnumProtocols32(IntPtr.Zero, buffer, ref length, out error)
                : WSCEnumProtocols(IntPtr.Zero, buffer, ref length, out error);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("ws2_32.dll", EntryPoint = "WSAStartup")]
        private static partial int WSAStartup(ushort version, IntPtr data);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("ws2_32.dll", EntryPoint = "WSACleanup")]
        private static partial int WSACleanup();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("ws2_32.dll", EntryPoint = "WSCEnumProtocols")]
        private static partial int WSCEnumProtocols(IntPtr protocols, IntPtr buffer, ref uint bufferLength, out int error);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("ws2_32.dll", EntryPoint = "WSCEnumProtocols32")]
        private static partial int WSCEnumProtocols32(IntPtr protocols, IntPtr buffer, ref uint bufferLength, out int error);
    }
}
