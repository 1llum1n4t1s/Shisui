using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>非表示・単独の LSP も含め、Winsock カタログを変更せずに調べる。</summary>
public interface IWinsockCatalogInspector
{
    /// <summary>必要な全カタログを列挙できない場合は IsComplete が false になる。</summary>
    /// <remarks>
    /// 結果をリセットの判断に使う呼び出し元は、検査から実行までネットワーク変更ゲートを保持する。
    /// このサービス自身はゲートを取得せず、結果をキャッシュしない。
    /// </remarks>
    Task<WinsockCatalogInspection> InspectAsync(CancellationToken ct = default);
}
