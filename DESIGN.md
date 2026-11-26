# Shisui 設計

Shisui の構造、責務、境界、不変条件の要約です。詳細は `docs/design/` の各文書に分けています。
作業コマンドとエージェント向け制約は [AGENTS.md](AGENTS.md)、機能別のコマンドやパーサーは
[references/architecture.md](references/architecture.md)、利用者向け情報は [README.md](README.md) を参照してください。

## 目的と配布範囲

DNS 設定、通信診断、アダプター情報、Windows の TCP・メンテナンス操作を一つの Avalonia UI で扱います。
正式な配布経路は Windows win-x64 のみで、macOS は実機検証・署名未実施です。詳細は [overview.md](docs/design/overview.md)。

## 主要コンポーネント

`Shisui.Core`（OS 非依存の抽象・サービス・builder/parser）、`Shisui.UI`（Avalonia・ViewModel・DI）、
`Shisui.Tests`、`scripts`（AOT publish・署名・配布）の4つです。詳細は [architecture.md](docs/design/architecture.md)。

## データフロー

### 起動

Velopack フック、旧版移行、自己昇格、`SingleInstanceGuard`、DI 構築の順で処理します。[workflow.md](docs/design/workflow.md)

### インストール移行の信頼境界

旧 PerUser 版は署名検証済みの PerMachine MSI へ移し、限定的な清掃だけを行います。[security.md](docs/design/security.md)

### ネットワーク操作

ViewModel が共有 `INetworkMutationGate` を取得し、service が builder で生成したコマンドを executor で実行します。[commands.md](docs/design/commands.md)

### 低遅延を優先する設定方針

遅延と損失回復の遅れを減らすことを優先し、設定の受理・実効値・実測を区別します。[configuration.md](docs/design/configuration.md)。ゲーム向け NIC 設定は [gaming-nic-settings.md](docs/design/gaming-nic-settings.md)、電源管理は [power-management.md](docs/design/power-management.md) にあります。

### 上り速度の制限

ActiveStore の QoS ポリシーで送信待ち行列を抑えます。おまかせ設定とは独立です。[uplink-limit.md](docs/design/uplink-limit.md)

### ネットワーク診断

ICMP の遅延診断（4/30/100 回の計測、p95、ジッター）と、その記録方法を扱います。ICMP 統計はゲームの実通信の保証に使いません。[troubleshooting.md](docs/design/troubleshooting.md)

### 設定と更新

設定は一時ファイル経由で保存し、読込み失敗時は保存を止めます（[data-model.md](docs/design/data-model.md)）。署名・更新・配信は [release-process.md](docs/design/release-process.md)。

## 重要な不変条件

引数は整形済み文字列で渡し、builder/parser は純粋処理とし、変更ゲートは非再入、QoS は `Shisui*` だけを扱い、旧版を管理者実行しません。[critical-invariants.md](docs/design/critical-invariants.md)

## 採用済みの設計判断

Core/UI/Tests 分離、`net11.0`、Native AOT、ロケール非依存出力、実行時昇格、CI 署名リリースの理由とトレードオフを [design-decisions.md](docs/design/design-decisions.md) にまとめています。
