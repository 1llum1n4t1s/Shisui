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
    private const string CategoryOptimization = "送信最適化";
    private const string CategoryPower = "電源・省電力";
    private const string CategoryUdp = "UDP オフロード";
    private const string CategoryReacquire = "IP アドレス再取得";
    private const string CategoryProxy = "プロキシ設定リセット";
    private const string CategoryStackReset = "ファイアウォール・スタックリセット (危険)";
    private const string CategoryComponent = "ネットワークコンポーネント再検出 (最終手段)";

    public static IReadOnlyList<WindowsMaintenanceCommand> All { get; } =
    [
        new(new MaintenanceCommandDefinition("nbtstat-purge-reload", CategoryCache, "NetBIOS 名前キャッシュを再読込", "キャッシュを消去し、ローカルの名前登録を再読込します。", false, false, true),
            "nbtstat", "-R"),
        new(new MaintenanceCommandDefinition("nbtstat-reregister", CategoryCache, "NetBIOS 名前を再登録", "WINS サーバーへの名前登録を更新します。", false, false),
            "nbtstat", "-RR"),
        new(new MaintenanceCommandDefinition("ipconfig-registerdns", CategoryCache, "DNS 登録を更新", "この PC の名前と IP アドレスを動的 DNS に登録します。", false, false),
            "ipconfig", "/registerdns"),
        new(new MaintenanceCommandDefinition("ipconfig-flushdns", CategoryCache, "DNS キャッシュをクリア", "保存された名前解決結果を消去します。", false, false),
            "ipconfig", "/flushdns"),
        new(new MaintenanceCommandDefinition("netsh-http-flush-logbuffer", CategoryCache, "HTTP ログバッファをフラッシュ", "メモリ上の HTTP ログをファイルへ書き出します。", false, false),
            "netsh", "http flush logbuffer"),
        new(new MaintenanceCommandDefinition("netsh-http-delete-cache", CategoryCache, "HTTP レスポンスキャッシュを削除", "保存された HTTP 応答を消去します。", false, false),
            "netsh", "http delete cache"),
        new(new MaintenanceCommandDefinition("arp-clear", CategoryCache, "ARP キャッシュをクリア", "全アダプタの IP アドレスと MAC アドレスの対応を消去します。", false, false, true),
            "netsh", "interface ipv4 delete arpcache"),
        new(new MaintenanceCommandDefinition("netsh-ipv4-delete-destinationcache", CategoryCache, "IPv4 経路キャッシュを削除", "学習済みの IPv4 の送信先経路を消去します。", false, false, true),
            "netsh", "interface ipv4 delete destinationcache"),
        new(new MaintenanceCommandDefinition("netsh-ipv6-delete-destinationcache", CategoryCache, "IPv6 経路キャッシュを削除", "学習済みの IPv6 の送信先経路を消去します。", false, false, true),
            "netsh", "interface ipv6 delete destinationcache"),
        new(new MaintenanceCommandDefinition("netsh-ipv6-delete-neighbors", CategoryCache, "IPv6 近隣探索キャッシュを削除", "IPv6 の近隣機器の情報を消去します。", false, false, true),
            "netsh", "interface ipv6 delete neighbors"),

        new(new MaintenanceCommandDefinition(
                "netsh-winsock-autotuning-on",
                CategoryOptimization,
                "Winsock 送信自動調整を有効化",
                "アプリの送信バッファーを通信状況に合わせて調整します。",
                false,
                false,
                true),
            "netsh", "winsock set autotuning on"),

        new(new MaintenanceCommandDefinition(
                "powercfg-pcie-link-state-ac-off",
                CategoryPower,
                "PCI Express のリンク状態電源管理を AC 時だけオフ",
                "現在の電源プランで、PC 全体の電源接続時の省電力を無効化します。バッテリー時は維持し、消費電力・発熱が増える場合があります。",
                false,
                false,
                true),
            "powercfg", WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments),

        new(new MaintenanceCommandDefinition(
                "powercfg-wifi-ac-max-performance",
                CategoryPower,
                "Wi-Fi を AC 時だけ最大パフォーマンスに設定",
                "現在の電源プランで、電源接続時の Wi-Fi 省電力を無効化します。バッテリー時は維持し、消費電力・発熱が増える場合があります。元の値を保存して復元できます。",
                false,
                false,
                true),
            "powercfg", WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments),
        new(new MaintenanceCommandDefinition(
                "powercfg-wifi-ac-restore",
                CategoryPower,
                "Wi-Fi の AC 設定を保存した元の値へ復元",
                "現在の電源プランの Wi-Fi を変更前の AC 値へ戻します。適用値と元の値のどちらでもない場合は上書きしません。バッテリー時は維持します。",
                false,
                false),
            "powercfg", WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments),

        // 全体 reset は行わず、対象を URO/USO に限定する。片方が非対応でも他方を実行できるよう分割する。
        new(new MaintenanceCommandDefinition("netsh-udp-uro-default", CategoryUdp,
                "UDP 受信オフロードを既定に戻す",
                "PC 全体の UDP 受信処理を Windows の既定値へ戻します。",
                false, false, true),
            "netsh", "interface udp set global uro=default"),
        new(new MaintenanceCommandDefinition("netsh-udp-uso-default", CategoryUdp,
                "UDP 送信オフロードを既定に戻す",
                "PC 全体の UDP 送信処理を Windows の既定値へ戻します。",
                false, false, true),
            "netsh", "interface udp set global uso=default"),

        new(new MaintenanceCommandDefinition("ipconfig-release", CategoryReacquire, "IPv4 アドレスを解放", "現在の IPv4 リースを手放します。再取得まで通信が切れます。", true, false),
            "ipconfig", "/release"),
        new(new MaintenanceCommandDefinition("ipconfig-release6", CategoryReacquire, "IPv6 アドレスを解放", "現在の IPv6 リースを手放します。再取得まで IPv6 通信が切れます。", true, false),
            "ipconfig", "/release6"),
        new(new MaintenanceCommandDefinition("ipconfig-renew", CategoryReacquire, "IPv4 アドレスを再取得", "DHCP から IPv4 アドレスを取得します。", false, false),
            "ipconfig", "/renew"),
        new(new MaintenanceCommandDefinition("ipconfig-renew6", CategoryReacquire, "IPv6 アドレスを再取得", "DHCP から IPv6 アドレスを取得します。", false, false),
            "ipconfig", "/renew6"),

        new(new MaintenanceCommandDefinition("netsh-winhttp-reset-proxy", CategoryProxy, "WinHTTP プロキシをリセット", "WinHTTP のプロキシ設定を削除し、直接接続へ戻します。", false, false),
            "netsh", "winhttp reset proxy"),
        new(new MaintenanceCommandDefinition("netsh-winhttp-reset-autoproxy", CategoryProxy, "WinHTTP 自動プロキシ設定をリセット", "WinHTTP の自動プロキシ検出状態をリセットします。", false, false),
            "netsh", "winhttp reset autoproxy"),

        new(new MaintenanceCommandDefinition("netsh-advfirewall-reset", CategoryStackReset, "ファイアウォール規則を初期化", "追加・変更した規則を削除し、Windows の既定値へ戻します。", true, false),
            "netsh", "advfirewall reset"),
        new(new MaintenanceCommandDefinition("netsh-winsock-reset", CategoryStackReset, "Winsock カタログをリセット", "通信機能の追加設定を初期化します。", true, true),
            "netsh", "winsock reset"),
        new(new MaintenanceCommandDefinition("netsh-int-tcp-reset", CategoryStackReset, "TCP 設定を既定値に戻す", "カスタム TCP 設定を削除し、Windows の既定値へ戻します。", true, false),
            "netsh", "int tcp reset"),
        new(new MaintenanceCommandDefinition("netsh-int-ip-reset", CategoryStackReset, "IP スタックをリセット", "TCP/IP 設定を初期化します。", true, true),
            "netsh", "int ip reset"),
        new(new MaintenanceCommandDefinition("route-clear", CategoryStackReset, "ルーティングテーブルをクリア", "ホスト・ループバック・マルチキャスト以外の経路を削除します。", true, false, true),
            "route", "/f"),

        new(new MaintenanceCommandDefinition("netcfg-delete", CategoryComponent, "ネットワークコンポーネントを再検出", "ネットワーク構成を削除し、再起動後に再検出します。通信が切れ、個別設定は失われます。", true, true),
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
