namespace Shisui.Core.Models;

/// <summary>全 Winsock カタログの検査結果。未完了時は LSP 不在を保証しない。</summary>
public sealed record WinsockCatalogInspection(
    bool IsComplete,
    bool HasLayeredProviders,
    string Detail);
