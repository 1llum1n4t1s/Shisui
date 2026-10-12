namespace Shisui.Core.Interfaces;

/// <summary>Winsock SPI の読み取り境界。カタログの書き換えは行わない。</summary>
public interface IWinsockCatalogApi
{
    bool Is64BitOperatingSystem { get; }
    bool Is64BitProcess { get; }

    /// <summary>成功時は 0 を返し、対応する Cleanup が必要になる。</summary>
    int Startup();

    /// <summary>成功した Startup を終了する。失敗時は例外を投げる。</summary>
    void Cleanup();

    /// <summary>全登録を WSCEnumProtocols または WSCEnumProtocols32 で列挙する。</summary>
    /// <remarks>buffer は WSAPROTOCOL_INFOW の連続領域。必要容量は length に返す。</remarks>
    int EnumProtocols(bool catalog32, nint buffer, ref uint length, out int error);
}
