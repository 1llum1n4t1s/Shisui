# アーキテクチャの読み先

現在の構造・責務・境界・不変条件の正本は [DESIGN.md](../../DESIGN.md)。実装領域ごとの詳細は [references/architecture.md](../../references/architecture.md)。**下の領域を触る前に必ず該当節を読む**。

| 触る対象 | 読む節 |
| --- | --- |
| プロジェクト構成、どこに何があるか | Project Structure |
| 外部コマンド実行（PowerShell / netsh） | Command execution model — string arguments, not `ArgumentList` |
| DI 登録、Windows 専用機能の扱い | DI: Windows-only features are optional dependencies |
| DNS プリセット、DoH / DoT | DNS Preset Catalog / DoH toggle / DoT toggle |
| ワンクリック最適化 | One-click optimization |
| ゲーム向け NIC 設定 | Gaming NIC profile |
| 上り速度の制限（QoS ポリシー、回線計測） | Uplink speed limit |
| 現在値の読み取り（ロケール非依存） | Reading current state is locale-independent |
| ネットワーク診断 | Used-PC network diagnostics / Network diagnostics |
| アダプタ一覧・詳細・ゴースト削除 | Adapter list filtering / Adapter details / Disconnected network device cleanup |
| 画面、テーマ、コントロール | UI Framework |
| 設定の保存、ログ | Settings & Logging |
