using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// 任意実行できる Windows ネットワークメンテナンスコマンドの定義一覧 (純粋データ、ユニットテスト対象)。
/// 元は `>nul 2>&1` 付きのバッチスクリプト由来だが、本アプリは標準出力/エラーをそのまま
/// UI のログパネルに表示するため、リダイレクト指定は持たない。
/// </summary>
public sealed record WindowsMaintenanceCommand(MaintenanceCommandDefinition Definition, string FileName, string Arguments);

public static class WindowsMaintenanceCommandCatalog
{
    private const string CategoryCache = "キャッシュ・登録";
    private const string CategoryReacquire = "IP アドレス再取得";
    private const string CategoryProxy = "プロキシ設定リセット";
    private const string CategoryStackReset = "ファイアウォール・スタックリセット (危険)";
    private const string CategoryComponent = "ネットワークコンポーネント再検出 (最終手段)";

    public static IReadOnlyList<WindowsMaintenanceCommand> All { get; } =
    [
        new(new MaintenanceCommandDefinition("nbtstat-purge-reload", CategoryCache, "NetBIOS 名前キャッシュを再読込", "nbtstat -R : キャッシュをパージしてローカル hosts から再読込する", false, false),
            "nbtstat", "-R"),
        new(new MaintenanceCommandDefinition("nbtstat-reregister", CategoryCache, "NetBIOS 名前を再登録", "nbtstat -RR : WINS サーバーへ名前を解放・再登録する", false, false),
            "nbtstat", "-RR"),
        new(new MaintenanceCommandDefinition("ipconfig-registerdns", CategoryCache, "DNS 登録を更新", "ipconfig /registerdns : DHCP リースを更新し DNS 名を再登録する", false, false),
            "ipconfig", "/registerdns"),
        new(new MaintenanceCommandDefinition("ipconfig-flushdns", CategoryCache, "DNS キャッシュをクリア", "ipconfig /flushdns : DNS リゾルバキャッシュを破棄する", false, false),
            "ipconfig", "/flushdns"),
        new(new MaintenanceCommandDefinition("netsh-http-flush-logbuffer", CategoryCache, "HTTP ログバッファをフラッシュ", "netsh http flush logbuffer", false, false),
            "netsh", "http flush logbuffer"),
        new(new MaintenanceCommandDefinition("netsh-http-delete-cache", CategoryCache, "HTTP レスポンスキャッシュを削除", "netsh http delete cache", false, false),
            "netsh", "http delete cache"),
        new(new MaintenanceCommandDefinition("arp-clear", CategoryCache, "ARP キャッシュをクリア", "arp -d * : ARP キャッシュの全エントリを削除する", false, false),
            "arp", "-d *"),
        new(new MaintenanceCommandDefinition("netsh-ipv4-delete-destinationcache", CategoryCache, "IPv4 経路キャッシュを削除", "netsh interface ipv4 delete destinationcache : 学習済みの次ホップ経路 (宛先キャッシュ) を削除する", false, false),
            "netsh", "interface ipv4 delete destinationcache"),
        new(new MaintenanceCommandDefinition("netsh-ipv6-delete-destinationcache", CategoryCache, "IPv6 経路キャッシュを削除", "netsh interface ipv6 delete destinationcache : 学習済みの次ホップ経路 (宛先キャッシュ) を削除する", false, false),
            "netsh", "interface ipv6 delete destinationcache"),
        new(new MaintenanceCommandDefinition("netsh-ipv6-delete-neighbors", CategoryCache, "IPv6 近隣探索キャッシュを削除", "netsh interface ipv6 delete neighbors : IPv6 近隣探索 (Neighbor Discovery) キャッシュを削除する (ARP の IPv6 版)", false, false),
            "netsh", "interface ipv6 delete neighbors"),

        new(new MaintenanceCommandDefinition("ipconfig-release", CategoryReacquire, "IPv4 アドレスを解放", "ipconfig /release : 現在の IPv4 リースを解放する (解放中は通信が切れる)", true, false),
            "ipconfig", "/release"),
        new(new MaintenanceCommandDefinition("ipconfig-release6", CategoryReacquire, "IPv6 アドレスを解放", "ipconfig /release6 : 現在の IPv6 リースを解放する (解放中は通信が切れる)", true, false),
            "ipconfig", "/release6"),
        new(new MaintenanceCommandDefinition("ipconfig-renew", CategoryReacquire, "IPv4 アドレスを再取得", "ipconfig /renew : DHCP から IPv4 アドレスを再取得する", false, false),
            "ipconfig", "/renew"),
        new(new MaintenanceCommandDefinition("ipconfig-renew6", CategoryReacquire, "IPv6 アドレスを再取得", "ipconfig /renew6 : DHCP から IPv6 アドレスを再取得する", false, false),
            "ipconfig", "/renew6"),

        new(new MaintenanceCommandDefinition("netsh-winhttp-reset-proxy", CategoryProxy, "WinHTTP プロキシをリセット", "netsh winhttp reset proxy", false, false),
            "netsh", "winhttp reset proxy"),
        new(new MaintenanceCommandDefinition("netsh-winhttp-reset-autoproxy", CategoryProxy, "WinHTTP 自動プロキシ設定をリセット", "netsh winhttp reset autoproxy", false, false),
            "netsh", "winhttp reset autoproxy"),

        new(new MaintenanceCommandDefinition("netsh-advfirewall-reset", CategoryStackReset, "ファイアウォール規則を初期化", "netsh advfirewall reset : 既定のファイアウォール規則に戻す (カスタム規則は消える)", true, false),
            "netsh", "advfirewall reset"),
        new(new MaintenanceCommandDefinition("netsh-winsock-reset", CategoryStackReset, "Winsock カタログをリセット", "netsh winsock reset : LSP チェーンを初期化する (要再起動)", true, true),
            "netsh", "winsock reset"),
        new(new MaintenanceCommandDefinition("netsh-int-tcp-reset", CategoryStackReset, "TCP スタックをリセット", "netsh int tcp reset : TCP/IP スタックの状態をリセットする (要再起動)", true, true),
            "netsh", "int tcp reset"),
        new(new MaintenanceCommandDefinition("netsh-int-tcp-set-global-default", CategoryStackReset, "TCP グローバル設定を既定値に戻す", "netsh int tcp set global default : RSS/ECN/RSC 等すべてのグローバル設定を初期値へ", true, false),
            "netsh", "int tcp set global default"),
        new(new MaintenanceCommandDefinition("netsh-int-ip-reset", CategoryStackReset, "IP スタックをリセット", "netsh int ip reset : TCP/IP レジストリ設定を初期化する (要再起動)", true, true),
            "netsh", "int ip reset"),
        new(new MaintenanceCommandDefinition("route-clear", CategoryStackReset, "ルーティングテーブルをクリア", "route /f : ホスト経路・ループバック・マルチキャスト以外の経路をすべて削除する", true, false),
            "route", "/f"),

        new(new MaintenanceCommandDefinition("netcfg-delete", CategoryComponent, "ネットワークコンポーネントを再検出", "netcfg -d : ネットワークコンポーネントを一旦すべて削除し、再起動後に Windows が再検出する。他の手段で直らない場合の最終手段", true, true),
            "netcfg", "-d"),
    ];

    public static WindowsMaintenanceCommand? Find(string id) => All.FirstOrDefault(c => c.Definition.Id == id);

    /// <summary>
    /// 「まとめて実行」に対応するカテゴリ → ボタンラベル。個別実行より一括実行の方が意味を持つ非破壊
    /// カテゴリだけを対象にする (IP 再取得は解放だけだと通信断になるので解放→再取得の順で走らせる等)。
    /// スタックリセット (危険) は全部まとめて叩くと事故になるため、コンポーネント再検出は単一コマンドの
    /// ため、いずれも一括対象に含めない。バッチは <see cref="All"/> の並び順で逐次実行される。
    /// </summary>
    public static IReadOnlyDictionary<string, string> BatchableCategoryLabels { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CategoryCache] = "すべてまとめて実行",
            [CategoryReacquire] = "まとめて再取得 (解放 → 再取得)",
            [CategoryProxy] = "まとめてリセット",
        };
}
