using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// DNS over TLS (DoT) の有効化/無効化を扱う抽象。Windows (netsh dnsclient) 専用機能のため
/// macOS では登録されない (DnsSettingsViewModel は null 許容で受け取り、非対応時は UI ごと隠す)。
/// </summary>
/// <remarks>
/// <see cref="IDohConfigurationService"/> と異なり状態読み取り (GetStatusAsync 相当) を持たない。
/// DoH には <c>Get-DnsClientDohServerAddress</c> という英語固定プロパティを返す PowerShell cmdlet が
/// 存在するが、DoT には対応する <c>Get-DnsClientDotServerAddress</c> のような cmdlet が存在しない
/// (2026-07 時点で調査済み)。ロケール依存の netsh テキスト出力をパースする経路はこのプロジェクトの
/// 「状態読み取りは PowerShell の英語固定プロパティのみ」という方針に反するため採用しない。
///
/// 上記の「ロケール依存」判断は 2026-07-06 に実機 (Windows, 日本語版) で実証済み:
/// <c>netsh dnsclient show encryption server=1.1.1.1</c> の出力ラベルは「DNS-over-HTTPS テンプレート」
/// 「自動アップグレード」「DNS-over-TLS ホスト」のように日本語化されており、英語 Windows と同じ形で
/// パースすることはできない。同じ実機テストで、同一 IP に対して DoH を登録した後に DoT を追加登録しても
/// DoH 側のレコードは消えず、2 つの独立した暗号化設定ブロックとして共存することも確認済み
/// (add encryption はプロトコルごとに独立してマージされ、上書きやエラーにはならない)。
///
/// 同一 IP に DoH/DoT 両方を有効化した場合の優先順位も 2026-07-06 に pktmon の実パケットキャプチャで
/// 検証済み: どちらか一方だけが選ばれるのではなく、両方とも実際にデータを運ぶ (排他的優先順位ではない)。
/// ただし接続の張り方が非対称: DoH (443) はテスト開始時に張った 2 本の接続を最後まで使い回す
/// (HTTP/2 的な持続接続) のに対し、DoT (853) はテスト中(約3秒間)に新規接続を約1.5〜1.8秒間隔で
/// 3 回張り直していた。平文 (ポート53) へのフォールバックは一切発生しなかった。この非対称性が
/// クエリ内容に連動しているのか Windows 側の独立した定期検証タイマーなのかまでは未確認。
///
/// 上記の接続の張り方の違いは応答速度にも表れることを 2026-07-06 に実測済み: 同一マシンで
/// 平文DNS/DoHのみ/DoTのみを切り替えて <c>Resolve-DnsName</c> を12回ずつ計測したところ、
/// 「DoT の方が仕組み上軽量で速い」という一般論に反し、DoH (平均53.2ms、最小-最大 49-59ms) の方が
/// DoT (平均57.7ms、最小-最大 48.8-64.6ms) よりわずかに速く、ばらつきも小さかった。DoH が接続を
/// 使い回すのに対し DoT は再接続のたびに TLS ハンドシェイクを踏むためと見られる。平文DNS も
/// ウォームアップ後 (初回を除く) は DoH と同程度の応答速度だった。差は数 ms 程度で、DNS 解決は
/// 対戦中の実トラフィックには関与しないため、レイテンシへの実利は限定的。
/// </remarks>
public interface IDotConfigurationService
{
    Task<IReadOnlyList<CommandExecutionResult>> EnableAsync(DnsServerSet servers, string dotHost, CancellationToken ct = default);

    Task<IReadOnlyList<CommandExecutionResult>> DisableAsync(DnsServerSet servers, CancellationToken ct = default);
}
