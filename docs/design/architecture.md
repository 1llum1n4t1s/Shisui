# 主要コンポーネント

| コンポーネント | 責務 | 境界 |
| --- | --- | --- |
| `src/Shisui.Core` | インターフェース、モデル、設定・ログの保存先、OS 別サービス、コマンド生成・解析 | UI に依存しない。OS 呼び出しは service、executor、ネイティブ API 実装に閉じ込める |
| `src/Shisui.UI` | Avalonia View、CommunityToolkit.Mvvm ViewModel、DI、起動・昇格・更新 UI | Core の抽象へ依存し、表示状態とユースケースの順序を管理する |
| `src/Shisui.Tests` | builder、parser、ViewModel、移行・排他制御の単体テスト | 実機のネットワーク設定を変更するコマンドは実行しない |
| `scripts` | Native AOT publish、Velopack MSI 生成、配置修正、署名、配布物検証、R2 公開 | Sigilune によるクラウド署名と、明示的なリリース操作（main の commit に付けた軽量タグ `v<版>` の push で起動する `release.yml` またはローカル実行）を前提にする |

`Shisui.Core` は、プラットフォーム共通のインターフェースに Windows の `netsh`、PowerShell、
`ipconfig`、`pnputil` 実装と、macOS の `networksetup`、`dscacheutil`、`ifconfig`、`ping`、
`traceroute` 実装を割り当てます。Windows 専用の TCP、メンテナンス、DoH/DoT、接続名整理、
ゲーム向け NIC 設定、上り速度の制限、使い込んだ PC 向け診断は Windows でだけ DI 登録し、ViewModel の省略可能依存関係と UI の可視性で分離します。

## 実行の流れ

1. ViewModel が入力と選択状態を固定し、ViewModel または複合操作を担う service が共有ゲート `INetworkMutationGate` のリースを取得します。
2. Core のインターフェースを実装する service が、純粋な builder/catalog でコマンドを生成します。
3. executor が外部プロセスを実行し、service または純粋な parser が結果をモデルへ変換します。Winsock 登録の検査と WinHTTP キャッシュ操作は、専用の API 抽象を介して Windows ネイティブ API を呼びます。
4. executor が開始・終了、所要時間、終了コード、標準出力・標準エラーをファイルへ記録します（[設定の保存とログ](data-model.md)）。

## 共有ゲート

DNS、DoH/DoT、TCP、MTU、メンテナンス、ゲーム向け NIC 設定、NIC 詳細設定初期化、接続名整理を ViewModel 横断で直列化します。読み取り専用の状態取得と診断はゲートを占有しません。おまかせ高速化設定は、同じリースの中で一連の処理を順に実行します。

関連文書: [DESIGN.md](../../DESIGN.md)、[references/architecture.md](../../references/architecture.md)
