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
| `src/Shisui.Core` | インターフェース、モデル、設定・ログの保存先、OS 別サービス、コマンド生成・解析 | UI に依存しない。OS 呼び出しは service、executor、ネイティブ API 実装に閉じ込める |
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

Release 版は UI 作成前にプロセス AppUserModelID を `velopack.Shisui` に設定し、
インストール済みショートカットと一致させます。Debug 版はこれを省略し、
Windows が開発版のタスクバーアイコンを製品ショートカットから解決することを避けます。
ネットワーク変更はシステム全体に及ぶため、実行時昇格は Debug 版でも行います。

### インストール移行の信頼境界

`WindowsPerMachineMigration` は旧 `%LocalAppData%\Shisui` 版を昇格する前に署名済み MSI へ移行します。
キャンセル時は終了し、ユーザー書込み可能な旧バイナリを管理者実行し続けません。
`ExecutableTrustVerifier` は System32 の WinVerifyTrust を使い、期待する publisher CN とチェーン全体の
失効状態を検証します。ダウンロードした MSI は Online、ローカル実行ファイルは CacheOnly を使い、
証明書は別のファイル読込みではなく検証済み trust chain から取得します。
MSI の同一ハンドルを署名検証から `msiexec` 終了まで保持する条件は「重要な不変条件」に示します。

信頼済み PerMachine 版は旧 `Update.exe` を実行せず、再解析ポイントを辿らない限定的な清掃を行います。
HKLM の安定実行ファイル登録、`.msi-installed`、現在の実行場所と署名 publisher が一致する場合だけ
旧ルートを扱います。MSI を直接入れた場合も、旧ルートに `Update.exe` や `packages` だけが残る場合も
同じ判定を使います。ロックされた項目以外の清掃を進め、pending marker と HKCU RunOnce で残りを再試行します。
旧実行ファイル・パッケージ・HKCU アンインストール登録・個人用ショートカットを整理し、
`%APPDATA%\Shisui` の設定とログは保持します。
管理者指定の独自インストール先は HKLM 登録・marker・署名の一致で受け入れ、勝手に移動しません。

誤配置修復は確認済みの `C:\Shisui` と `Program Files\ゆろち\Shisui` だけを対象にし、
同意後に固定配信元の署名済み MSI で `Program Files\Shisui` へ移します。
MSI 実行前に正確な旧ルートを管理者書込み限定の HKLM marker へ記録し、RunOnce で清掃を引き継ぎます。
同じ ProductCode の保守が何も変更しなければ `REINSTALL=ALL REINSTALLMODE=vamus` で再実行します。

旧 `StartMenu\ゆろち\Shisui.lnk` は更新フックでルートへ移し、通常起動でも再試行します。
既存ルートのショートカットは上書きせず、旧フォルダーは空のときだけ取り除きます。
移動と旧 PerUser 清掃は Programs フォルダーへ再帰的な `SHChangeNotify(SHCNE_UPDATEDIR)` を送り、
Windows Start の重複キャッシュを更新します。

### ネットワーク操作

1. ViewModel が入力と選択状態を固定し、ViewModel または複合操作を担う service が変更操作の共有
   `INetworkMutationGate` リースを取得します。
2. Core のインターフェースを実装する service が、純粋な builder/catalog でコマンドを生成します。
3. executor が外部プロセスを実行し、service または純粋な parser が結果をモデルへ変換します。
   Winsock 登録の検査と WinHTTP キャッシュ操作は、専用の API 抽象を介して Windows ネイティブ API を呼びます。
4. executor が実行時点の開始・終了、所要時間、終了コード、標準出力・標準エラーをファイルへ記録します。
   ViewModel は従来の簡潔な画面ログを維持し、通知を実行IDで関連付けます。適用後確認などの合成結果は
   成功・失敗を問わず両方の出力をファイルへ記録します。

共有ゲートは DNS、DoH/DoT、TCP、MTU、メンテナンス、ゲーム向け NIC 設定、NIC 詳細設定初期化、接続名整理を
ViewModel 横断で直列化します。読み取り専用の状態取得と診断はゲートを占有しません。
「おまかせ高速化設定」は同じリース内で条件付き Winsock リセットと WinHTTP 自動プロキシキャッシュ消去を先に行い、選択 NIC の DHCP 解放・再取得、DNS、許可リスト化されたキャッシュ処理と UDP オフロード既定化・PCI Express 省電力の AC 時無効化、TCP 既定化と BBR2・RACK/TLP・RSS 有効化、低遅延を優先する RSC 無効化、選択物理 NIC のゲーム向け設定、
切断済みデバイス整理を順番に実行し、接続名を参照する処理が終わった後にだけ名前整理を行います。
Wi-Fi の電源設定は現在のプランの AC 値だけを最大パフォーマンスへ変更し、DC 値を読み戻して保持を確認します。
変更前の AC 値は電源プラン GUID ごとに AppSettings の `WifiPowerPlanSnapshots` へ保存し、再適用でも上書きせず、メンテナンスの復元操作で戻せます。
復元時に現在値が適用値・元値のどちらでもない場合は外部変更を保護します。未確認の設定や変更中のプラン切替は成功として扱いません。
Winsock SPI で非表示・単独 LSP を含む登録を列挙し、64 ビット OS では 32 ビット側も確認します。
両カタログを完全に確認して全件が基底プロバイダーの場合だけリセットします。LSP があれば保護して省略し、確認不能は失敗として残します。
手動メンテナンスの Winsock リセットは従来どおりです。条件付き修復は送信自動調整・TCP/NIC 設定より先に実行します。
WinHTTP は `WinHttpResetAutoProxy` のキャッシュ専用フラグ `0x2000C` だけを許可し、保存済みプロキシ設定を書き換えません。
同一サービス内で試行間隔を 30 秒以上に制限し、受付済み直後の再実行は省略、前回失敗直後の再実行は未完了として表示します。
API の受付結果と実際のキャッシュ消去は区別し、消去の実測は行いません。
ルート表クリア（`route /f`）は通常のメンテナンスループで保留し、名前整理を含む他の変更処理が終わった最後の変更操作として一度だけ実行します。
PC 全体に影響して通信が切れるため、完了後は再起動を案内します。失敗・例外は全体の失敗としてログに残し、この処理まで同じリースを保持します。
BBR2 は全 5 テンプレート、RACK/TLP は Compat を除く 4 テンプレートへ適用します。UDP は URO/USO のみを既定化し、
UDP 全体の reset は行いません。ECN などの詳細値は Windows 既定を維持します。適用後は同じリース内で BBR2、RSS 有効・RSC 無効のグローバル状態と Internet の受信ウィンドウ実効値を読み戻し、
ポリシー上書き・不完全取得を成功扱いしません。RACK/TLP は日英の既知ラベルで両方の有効状態を確認できた場合、
再設定せず変更不要とします。未確認・無効の場合は設定コマンドの結果を保持します。UDP などはコマンド受付結果のみです。
DHCP は名前・GUID・InterfaceIndex が一致した選択 NIC に限定し、固定 IP と不明状態は解放しません。
DHCP の利用状態が不明な場合は確認不能の失敗結果を残し、既知の対象外として成功表示しません。
IPv4 は DHCP 有効時、IPv6 は有効な取得済み DHCPv6 リースがある場合だけ処理します。
解放は 15 秒、更新・回復は独立した 60 秒の期限を持ち、解放の失敗・キャンセル後も更新を試みます。
更新後は同じ NIC の DHCP 由来の有効な IP と既存デフォルトゲートウェイの回復を確認し、不明・未回復を失敗として残します。
PCI Express は現在の電源プランのリンク状態の電源管理だけを対象に、AC 値をオフにします。
変更前の取得に失敗した場合は書き込まず、適用後は AC 値がオフ・DC 値が変更前と同じであることを確認します。
電源プランの切り替えや DC 値の書き込みは行いません。メンテナンス画面からも個別に実行できます。
選択した NIC 以外の PCI Express 機器にも影響し、消費電力・発熱が増える可能性を画面に表示します。

### ゲーム向け NIC 設定

Windows専用のゲーム向けNIC設定は「おまかせ高速化設定」に含めます。個別適用と保存した元の値への復元も引き続き利用できます。
物理 Ethernet/Wi-Fi の対応する標準プロパティの RSS・LSOv2 IPv4/IPv6 を 1、IP IPv4・TCP/UDP IPv4/IPv6 のチェックサムオフロードを 3（送受信有効）にします。
二値項目は 0/1 の両方、チェックサム項目は元値と目標値がドライバーの対応値に含まれることを確認します。受信処理や CPU 負荷への効果と、ゲーム・WAN の遅延改善は区別します。
物理Ethernetの `*InterruptModeration` / `*EEE`、物理Wi-Fiの `*InterruptModeration` を、対応値を確認して無効化します。
さらに、プロバイダー名またはPCI/USBベンダーIDからRealtek製と確認できる物理Ethernetでは、プロパティが存在して0/1をサポートする場合に `EnableGreenEthernet` / `GigaLite` / `PowerSavingMode` を扱います。これらの0/1定義はRTL8168HのINFで確認しています。
ドライバープロバイダーとPCI vendor IDを確認できるMediaTek無線NICでは、定義を確認済みの `LowPowerEnable` / `UAPSDSupport` を扱います。
媒体・ベンダーごとの許可リストをquery・parser・書込み・復元で照合し、EEEをWi-Fiへ、メーカー独自項目を他社へ流用しません。
個別適用・復元ではserviceが共有ゲートを取得します。一括適用は呼出し元が同じ共有リースを保持したまま ApplyWithinMutationLeaseAsync を呼び、ゲートを再取得せず、接続名整理より前に実行します。どちらも変更前の値をAppSettingsへ保存してから書き込み、設定値を再取得します。一括適用はNIC設定の失敗・空結果・例外も全体の失敗結果へ含めます。
GUIDと説明で対象を照合し、再適用で元の値を上書きしません。復元は保存対象だけを扱い、外部変更との競合は上書きしません。
`-NoRestart` により自動切断を避け、ドライバー設定として保存された値の読み戻しと、稼働中の反映・性能の確認を区別します。適用・復元後はPC再起動が必要です。
NIC設定の適用処理は、仮想NIC、未知のプロパティ値、許可対象外のオフロード・リンク速度・バッファー・周波数帯・チャンネル幅・ローミングを変更しません。一括処理のTCPグローバル設定とは責務を分けます。

### ネットワーク診断

遅延診断は4/30/100回のICMP計測に対応し、Windowsでは全試行の成功・失敗を記録します。
p95は成功応答のnearest-rank、ジッターは連続成功間のRTT差の絶対値の平均で、失敗をまたぐ差を含めません。
測定はOSの経路を使い、NIC選択には紐付きません。ICMP統計はゲームの実通信や微小なNIC内遅延の保証には使いません。

### 設定と更新

`SettingsService` は `AppSettings` を OS 標準のアプリデータ領域へ保存します。保存は semaphore で直列化し、
一時ファイルを書いてから置換します。Native AOT での実行を保証するため、JSON は
`ShisuiJsonContext` のソース生成メタデータだけを使います。更新 URL と channel は `[JsonIgnore]` の
読み取り専用値であり、利用者の `settings.json` から外部ホストへ差し替えられません。

`UpdateService` は固定配信元の `SimpleWebSource` と `UpdateManager` を構築し、
インストール版でだけ manager を返します。確認・ダウンロード・適用 UI は
`VelopackUpdateDialog.Avalonia` が担当し、`VersionViewModel` が owner と表示タイミングを管理します。
日本語の `ShisuiUpdateStrings` とアクセント `#0A84FF` を使い、
スキップ対象は `AppSettings.IgnoreUpdateTag` に保存します。手動確認は最新でも結果を表示します。
起動時確認は `CheckForUpdatesOnStartup` に従い、MainWindow が確定するよう
`Dispatcher.UIThread.Post(Background)` で遅延させます。開発・未インストール版では確認をスキップします。

Windows リリースはローカルで win-x64 Native AOT publish、同じ runtime の Velopack PerMachine MSI
生成、`ProgramFiles64Folder\Shisui` への Directory 表正規化、Authenticode 再署名・検証を行います。
この正規化は旧 Velopack の `TARGETDIR\INSTALLFOLDER` 配置問題を防ぎ、移行時の
`VELOPACK_INSTALLDIR` 指定も同じ配置を補強します。更新と CLI の固定バージョンを揃え、
MSI レイアウトの変化を署名済み配布経路で検証する理由です。

配信元は R2 バケット `shisui-updates` の `https://shisui.kagayoi.com` です。
channel は `win`、manifest は `releases.win.json`、移行用の固定ファイルは `Shisui-win.msi` です。
GitHub Releases は配信に使いません。
公開は配信中 metadata の退避、版付き nupkg、固定名バイナリ、更新 metadata の順です。
Cloudflare cache purge 後に manifest 一致と固定 MSI の配信サイズを確認し、
metadata 公開後の失敗では退避 metadata を復元して再 purge します。
世代整理は manifest が参照する全オブジェクトと最新2版の版付き配布物を保持します。
PerUser Setup.exe は公開対象から除外し、過去の配信物とキャッシュは MSI の伝播成功後にだけ整理します。
署名認証と実行コマンドは [AGENTS.md](AGENTS.md#auto-update--release) を参照してください。

## 重要な不変条件

- `ICommandExecutor` は配列ではなく整形済み引数文字列を受け取ります。`netsh` が生のコマンドラインを
  独自解析するため、adapter 名の `name="..."` を .NET の `ArgumentList` で再エスケープしません。
- DNS アドレスはコマンド生成前に IPv4/IPv6 として検証し、文字列引数へ埋め込む値の引用符を拒否します。
- PowerShell でアダプター名を扱うときは、`Get-NetAdapter -Name` のワイルドカード解釈へ直接渡さず、
  全アダプターから大文字小文字を区別しない完全一致で一意に解決します。後続 cmdlet の `-Name` へ渡す場合も
  ワイルドカードをエスケープし、意図しない複数アダプターの読み取り・変更を防ぎます。
- Windows の現在値はローカライズされた `netsh` 表示を解析せず、PowerShell で固定した `KEY=VALUE`
  または XML を解析します。標準出力は raw byte を同時に読み、厳密 UTF-8、次に OEM code page の順で復号します。
  例外として RACK/TLP の変更不要判定だけは、PowerShell に値がないため確認済みの日英ラベルを限定解析します。
  未知の言語・不完全な出力を有効と推測せず、設定コマンドの失敗を成功へ置き換えません。
- Windows の外部コマンドは既知のシステムコマンドだけを絶対パスへ解決し、未知の相対実行ファイルを拒否します。
- executor のキャンセル時と開始後の例外時は子プロセスツリー全体を終了させ、変更コマンドをバックグラウンドへ残しません。
- builder、catalog、parser は OS を呼ばない純粋処理です。OS アクセスは service、executor、専用のネイティブ API 実装の責務です。
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
