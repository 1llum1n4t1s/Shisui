# Shisui 設計

この文書は、現在の実装から確認できる Shisui の構造、責務、境界、不変条件をまとめた設計の正本です。
作業コマンドとエージェント向け制約は [AGENTS.md](AGENTS.md)、各機能のコマンドやパーサーの詳細は
[references/architecture.md](references/architecture.md)、利用者向け情報は [README.md](README.md) を参照してください。

## 目的と配布範囲

Shisui は、DNS 設定、通信診断、ネットワークアダプター情報、Windows の TCP・メンテナンス操作を
一つの Avalonia デスクトップ UI から扱うアプリです。コードは Windows と macOS を対象に
`net10.0` で構成されていますが、署名・インストーラー・自動更新を含む正式な配布経路は Windows
win-x64 のみです。macOS 実装はコンパイルとパーサー単体テストまでで、実機検証と署名・notarization は行われていません。

## 主要コンポーネント

| コンポーネント | 責務 | 境界 |
| --- | --- | --- |
| `src/Shisui.Core` | インターフェース、モデル、設定・ログの保存先、OS 別サービス、コマンド生成・解析 | UI に依存しない。OS 呼び出しは `*Service` と executor に閉じ込める |
| `src/Shisui.UI` | Avalonia View、CommunityToolkit.Mvvm ViewModel、DI、起動・昇格・更新 UI | Core の抽象へ依存し、表示状態とユースケースの順序を管理する |
| `src/Shisui.Tests` | builder、parser、ViewModel、移行・排他制御の単体テスト | 実機のネットワーク設定を変更するコマンドは実行しない |
| `scripts` | Native AOT publish、Velopack MSI 生成、配置修正、署名、R2 公開・検証・世代整理 | ローカル署名環境と明示的なリリース操作を前提にする |
| `web` | `/` の製品ページと R2 更新ファイルへの透過的な委譲 | 更新ファイルのレスポンスを加工しない |

`Shisui.Core` は、プラットフォーム共通のインターフェースに Windows の `netsh`、PowerShell、
`ipconfig`、`pnputil` 実装と、macOS の `networksetup`、`dscacheutil`、`ifconfig`、`ping`、
`traceroute` 実装を割り当てます。Windows 専用の TCP、メンテナンス、DoH/DoT、接続名整理、
使い込んだ PC 向け診断は Windows でだけ DI 登録し、ViewModel の省略可能依存関係と UI の可視性で分離します。

## データフロー

### 起動

1. `Program` が Velopack の内部フックを UI、昇格、多重起動判定より先に処理します。
2. Windows では旧スタートメニューを移行し、必要なら署名済み PerMachine MSI への移行・既知の誤配置修復を行います。
3. `asInvoker` で開始した通常プロセスを `runas` で自己昇格してから `SingleInstanceGuard` を取得します。
4. `App.axaml.cs` が OS に応じたサービスを DI 登録し、`MainWindowViewModel` と画面を構築します。

この順序により、Velopack のフックを昇格要求で壊さず、非昇格プロセスが単一起動ロックを保持したまま
昇格後プロセスを妨げる状態を避けます。macOS はアプリ全体を昇格せず、DNS 変更とキャッシュ消去だけを
`osascript` 経由で管理者実行します。

### ネットワーク操作

1. ViewModel が入力と選択状態を固定し、変更操作では共有 `INetworkMutationGate` のリースを取得します。
2. OS 非依存インターフェースの service が、純粋な builder/catalog でコマンドを生成します。
3. executor が外部プロセスを実行し、service または純粋な parser が結果をモデルへ変換します。
4. ViewModel が状態を更新し、`CommandExecutionResult` を画面下部の実行ログとファイルログへ渡します。

共有ゲートは DNS、DoH/DoT、TCP、MTU、メンテナンス、NIC 詳細設定初期化、接続名整理を
ViewModel 横断で直列化します。読み取り専用の状態取得と診断はゲートを占有しません。
「おまかせ高速化設定」は同じリース内で DNS、許可リスト化されたキャッシュ処理、TCP 既定化、
切断済みデバイス整理を順番に実行し、接続名を参照する処理が終わった後にだけ名前整理を行います。

### 設定と更新

`SettingsService` は `AppSettings` を OS 標準のアプリデータ領域へ保存します。保存は semaphore で直列化し、
一時ファイルを書いてから置換します。Native AOT での実行を保証するため、JSON は
`ShisuiJsonContext` のソース生成メタデータだけを使います。更新 URL と channel は `[JsonIgnore]` の
読み取り専用値であり、利用者の `settings.json` から外部ホストへ差し替えられません。

Windows リリースはローカルで win-x64 Native AOT publish、Velopack PerMachine MSI 生成、
`ProgramFiles64Folder\Shisui` への配置修正、Authenticode 署名、R2 公開を行います。
ランディング Worker は `/` と `/index.html` だけを返し、それ以外の MSI、nupkg、manifest は
同一ホストの R2 origin へ透過的に委譲します。

## 重要な不変条件

- `ICommandExecutor` は配列ではなく整形済み引数文字列を受け取ります。`netsh` が生のコマンドラインを
  独自解析するため、adapter 名の `name="..."` を .NET の `ArgumentList` で再エスケープしません。
- DNS アドレスはコマンド生成前に IPv4/IPv6 として検証し、文字列引数へ埋め込む値の引用符を拒否します。
- Windows の現在値はローカライズされた `netsh` 表示を解析せず、PowerShell で固定した `KEY=VALUE`
  または XML を解析します。標準出力は raw byte を同時に読み、厳密 UTF-8、次に OEM code page の順で復号します。
- executor のキャンセル時は子プロセスツリー全体を終了させ、変更コマンドをバックグラウンドへ残しません。
- builder、catalog、parser は OS を呼ばない純粋処理として維持します。OS アクセスは service と executor の責務です。
- `INetworkMutationGate` は非再入です。複合操作は外側で一度だけ取得し、内側から再取得しません。
- 公式 DNS プリセットの IP、DoH template、DoT host は一体の契約です。特に Cloudflare のフィルタ段階ごとの
  hostname を標準 hostname へ置き換えません。NextDNS とカスタム設定には固定の暗号化 DNS endpoint を与えません。
- DoH のチェック状態は OS の実状態から復元します。DoT はロケール非依存に状態取得できる API がないため、
  保存状態を実状態として表示せず、操作時だけ有効化・無効化します。
- Windows の正式配布物は署名済み PerMachine MSI です。PerUser `Setup.exe` は公開せず、旧 PerUser 版を
  ユーザー書き込み可能な場所から管理者実行し続けません。移行・修復対象は検証済みの既知パスに限定します。
- 自動更新元は `https://shisui.kagayoi.com` の `releases.win.json` です。旧
  `shisui.nephilim.jp` は出荷済みクライアントのため期限まで配信を維持します。

## 採用済みの設計判断

- **Core/UI/Tests の分離**: OS コマンドの組み立てと解析を UI から外し、実機を変更せずに大半をテストできます。
  その代わり、ViewModel はユースケースの順序と排他境界を明示的に管理します。
- **プラットフォーム中立な `net10.0`**: 一つのモデルと UI を共有しつつ、OS 固有機能を DI で差し替えます。
  Windows 専用機能は macOS で stub を返さず UI ごと隠すため、利用可能範囲が明確になります。
- **Native AOT**: JIT を含まない単独配布と起動経路を採用しています。反面、reflection 依存を避け、
  JSON source generation と実配布条件での publish 検証が必要です。
- **ロケール非依存の機械可読出力**: 表示用コマンドの文字列解析よりコマンド数や builder/parser が増えますが、
  日本語・英語 Windows と console encoding の差で状態判定が壊れることを避けます。
- **実行時昇格**: `requireAdministrator` manifest ではなく Velopack フック後の自己昇格を選び、
  インストーラー互換性と一度だけの UAC を両立します。
- **ローカル署名リリース**: SimplySign の対話認証が必要なため CI では署名せず、再現可能な固定 CLI と
  検証・ロールバックを含む `release-local.ps1` に配布責務を集約します。
