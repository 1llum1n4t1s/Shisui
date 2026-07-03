# プロジェクト構成

## ディレクトリ構造

- `src/`: ソースコード
  - `Shisui.Core/`: 共通ロジック、インターフェース、データモデル、OS別サービス実装 (UI 非依存)
  - `Shisui.UI/`: Avalonia アプリケーション本体 (メイン画面、各タブ)
  - `Shisui.Tests/`: MSTest ユニットテスト (コマンド組み立てロジック中心)
- `docs/`: ドキュメント

## 主要コンポーネント

1. **INetworkAdapterService**: ネットワークアダプタ (Windows) / ネットワークサービス (macOS) の一覧取得
2. **IDnsConfigurationService**: DNS サーバーの適用・自動取得への復元
3. **IDnsCacheService**: DNS キャッシュのクリア
4. **ITcpTuningService** (Windows専用): BBR2 輻輳制御・TCP グローバル設定の調整
5. **INetworkMaintenanceService** (Windows専用): 任意実行メンテナンスコマンド群
6. **ICommandExecutor**: 外部プロセス実行の抽象。Windows はそのまま起動、macOS は AppleScript 経由で
   コマンドごとに管理者権限昇格をリクエストする

## OS 別実装の切り替え

`App.axaml.cs` の `ConfigureServices` が `OperatingSystem.IsWindows()` / `IsMacOS()` で実装を切り替える。
BBR2 / TCP 詳細設定 / メンテナンスコマンドは Windows の `netsh` に強く依存する機能のため、macOS では
`ITcpTuningService` / `INetworkMaintenanceService` を DI 登録せず、対応する画面タブも表示しない。
