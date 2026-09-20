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

`Shisui.Core` は、プラットフォーム共通のインターフェースに Windows の `netsh`、PowerShell、
`ipconfig`、`pnputil` 実装と、macOS の `networksetup`、`dscacheutil`、`ifconfig`、`ping`、
`traceroute` 実装を割り当てます。Windows 専用の TCP、メンテナンス、DoH/DoT、接続名整理、
ゲーム向け NIC 設定、使い込んだ PC 向け診断は Windows でだけ DI 登録し、ViewModel の省略可能依存関係と UI の可視性で分離します。

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

1. ViewModel が入力と選択状態を固定し、ViewModel または複合操作を担う service が変更操作の共有
   `INetworkMutationGate` リースを取得します。
2. OS 非依存インターフェースの service が、純粋な builder/catalog でコマンドを生成します。
3. executor が外部プロセスを実行し、service または純粋な parser が結果をモデルへ変換します。
4. executor が実行時点の開始・終了、所要時間、終了コード、標準出力・標準エラーをファイルへ記録します。
   ViewModel は従来の簡潔な画面ログを維持し、通知を実行IDで関連付けます。適用後確認などの合成結果は
   成功・失敗を問わず両方の出力をファイルへ記録します。

共有ゲートは DNS、DoH/DoT、TCP、MTU、メンテナンス、ゲーム向け NIC 設定、NIC 詳細設定初期化、接続名整理を
ViewModel 横断で直列化します。読み取り専用の状態取得と診断はゲートを占有しません。
「おまかせ高速化設定」は同じリース内で DNS、許可リスト化されたキャッシュ処理と UDP オフロード既定化・PCI Express 省電力の AC 時無効化、TCP 既定化と BBR2・RACK/TLP 有効化、
切断済みデバイス整理を順番に実行し、接続名を参照する処理が終わった後にだけ名前整理を行います。
BBR2 は全 5 テンプレート、RACK/TLP は Compat を除く 4 テンプレートへ適用します。UDP は URO/USO のみを既定化し、
UDP 全体の reset は行いません。適用後は同じリース内で BBR2 と Internet の受信ウィンドウ実効値を読み戻し、
ポリシー上書き・不完全取得を成功扱いしません。RACK/TLP は日英の既知ラベルで両方の有効状態を確認できた場合、
再設定せず変更不要とします。未確認・無効の場合は設定コマンドの結果を保持します。UDP などはコマンド受付結果のみです。
PCI Express は現在の電源プランのリンク状態の電源管理だけを対象に、AC 値をオフにします。
変更前の取得に失敗した場合は書き込まず、適用後は AC 値がオフ・DC 値が変更前と同じであることを確認します。
電源プランの切り替えや DC 値の書き込みは行いません。メンテナンス画面からも個別に実行できます。
選択した NIC 以外の PCI Express 機器にも影響し、消費電力・発熱が増える可能性を画面に表示します。

### ゲーム向け NIC 設定

ゲーム向けの追加NIC設定は通常のおまかせ設定とは独立したWindows専用操作です。
物理Ethernetの `*InterruptModeration` / `*EEE`、物理Wi-Fiの `*InterruptModeration` を、対応値を確認して無効化します。
さらに、ドライバープロバイダーとPCI vendor IDを確認できるMediaTek無線NICでは、定義を確認済みの `LowPowerEnable` / `UAPSDSupport` を扱います。
媒体・ベンダーごとの許可リストをquery・parser・書込み・復元で照合し、EEEをWi-Fiへ、メーカー独自項目を他社へ流用しません。
serviceが共有ゲートを取得し、変更前の値をAppSettingsへ保存してから書き込み、設定値を再取得します。
GUIDと説明で対象を照合し、再適用で元の値を上書きしません。復元は保存対象だけを扱い、外部変更との競合は上書きしません。
`-NoRestart` により自動切断を避け、ドライバー設定として保存された値の読み戻しと、稼働中の反映・性能の確認を区別します。適用・復元後はPC再起動が必要です。
仮想NIC、未知のプロパティ値、RSS・オフロード・リンク速度・バッファー・周波数帯・チャンネル幅・ローミングは変更しません。

### ネットワーク診断

遅延診断は4/30/100回のICMP計測に対応し、Windowsでは全試行の成功・失敗を記録します。
p95は成功応答のnearest-rank、ジッターは連続成功間のRTT差の絶対値の平均で、失敗をまたぐ差を含めません。
測定はOSの経路を使い、NIC選択には紐付きません。ICMP統計はゲームの実通信や微小なNIC内遅延の保証には使いません。

### 設定と更新

`SettingsService` は `AppSettings` を OS 標準のアプリデータ領域へ保存します。保存は semaphore で直列化し、
一時ファイルを書いてから置換します。Native AOT での実行を保証するため、JSON は
`ShisuiJsonContext` のソース生成メタデータだけを使います。更新 URL と channel は `[JsonIgnore]` の
読み取り専用値であり、利用者の `settings.json` から外部ホストへ差し替えられません。

Windows リリースはローカルで win-x64 Native AOT publish、Velopack PerMachine MSI 生成、
`ProgramFiles64Folder\Shisui` への配置修正、Authenticode 署名、R2 公開を行います。
更新クライアントと移行処理は、固定された R2 カスタムドメイン `https://shisui.kagayoi.com` から
manifest と署名済み MSI を取得します。

## 重要な不変条件

- `ICommandExecutor` は配列ではなく整形済み引数文字列を受け取ります。`netsh` が生のコマンドラインを
  独自解析するため、adapter 名の `name="..."` を .NET の `ArgumentList` で再エスケープしません。
- DNS アドレスはコマンド生成前に IPv4/IPv6 として検証し、文字列引数へ埋め込む値の引用符を拒否します。
- Windows の現在値はローカライズされた `netsh` 表示を解析せず、PowerShell で固定した `KEY=VALUE`
  または XML を解析します。標準出力は raw byte を同時に読み、厳密 UTF-8、次に OEM code page の順で復号します。
  例外として RACK/TLP の変更不要判定だけは、PowerShell に値がないため確認済みの日英ラベルを限定解析します。
  未知の言語・不完全な出力を有効と推測せず、設定コマンドの失敗を成功へ置き換えません。
- Windows の外部コマンドは既知のシステムコマンドだけを絶対パスへ解決し、未知の相対実行ファイルを拒否します。
- executor のキャンセル時と開始後の例外時は子プロセスツリー全体を終了させ、変更コマンドをバックグラウンドへ残しません。
- builder、catalog、parser は OS を呼ばない純粋処理として維持します。OS アクセスは service と executor の責務です。
- `INetworkMutationGate` は非再入です。複合操作は外側で一度だけ取得し、内側から再取得しません。
- 公式 DNS プリセットの IP、DoH template、DoT host は一体の契約です。特に Cloudflare のフィルタ段階ごとの
  hostname を標準 hostname へ置き換えません。NextDNS とカスタム設定には固定の暗号化 DNS endpoint を与えません。
- DoH のチェック状態は OS の実状態から復元します。DoT はロケール非依存に状態取得できる API がないため、
  保存状態を実状態として表示せず、操作時だけ有効化・無効化します。
- Windows の正式配布物は署名済み PerMachine MSI です。PerUser `Setup.exe` は公開せず、旧 PerUser 版を
  ユーザー書き込み可能な場所から管理者実行し続けません。移行・修復対象は検証済みの既知パスに限定します。
  ダウンロードした MSI は書換え・削除を拒否する同一ハンドルで署名検証し、そのハンドルから再解析ポイントを解決した最終パスだけを `msiexec` へ渡して終了まで保持します。
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
