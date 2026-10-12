namespace Shisui.Core.Interfaces;

/// <summary>WinHTTP の自動プロキシキャッシュ消去 API。戻り値は Windows エラーコード。</summary>
public interface IWinHttpAutoProxyApi
{
    uint ResetAutoProxy(uint flags);
}
