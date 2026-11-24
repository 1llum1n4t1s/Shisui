# 更新と配布

`UpdateService` は固定配信元の `SimpleWebSource` と `UpdateManager` を構築し、
インストール版でだけ manager を返します。確認・ダウンロード・適用 UI は
`VelopackUpdateDialog.Avalonia` が担当し、`VersionViewModel` が owner と表示タイミングを管理します。
日本語の `ShisuiUpdateStrings` とアクセント `#0A84FF` を使い、
スキップ対象は `AppSettings.IgnoreUpdateTag` に保存します。手動確認は最新でも結果を表示します。
起動時確認は `CheckForUpdatesOnStartup` に従い、MainWindow が確定するよう
`Dispatcher.UIThread.Post(Background)` で遅延させます。開発・未インストール版では確認をスキップします。

Windows リリースはクリーンな作業ツリーと HEAD に一致する完全な source SHA、.NET 11 SDK を前提に、
GitHub Actions の `release.yml`（軽量タグ `v<版>` の push で起動・Environment `release`。guard job がタグ・commit・版を検査する）またはローカルの同じスクリプトで、
win-x64 Native AOT publish、同じ runtime の Velopack PerMachine MSI
生成、`ProgramFiles64Folder\Shisui` への Directory 表正規化、Authenticode 再署名・検証を行います。
署名は Sigilune が Certum のクラウド鍵で行い、補正で無効になった MSI の署名は `sigilune sign --replace` で置き換えます。
この正規化は旧 Velopack の `TARGETDIR\INSTALLFOLDER` 配置問題を防ぎ、移行時の
`VELOPACK_INSTALLDIR` 指定も同じ配置を補強します。更新と CLI の固定バージョンを揃え、
MSI レイアウトの変化を署名済み配布経路で検証する理由です。

署名後の最終配布物は `verify-release-artifacts.ps1` が検証します。NUPKG 内の EXE は
版と source SHA、署名者、AMD64 PE と CLR header 不在を照合し、nuspec の RID・channel・実行ファイル名も確認します。
MSI は署名・版・x64・PerMachine 属性・Directory 表を、manifest は単一の完全 NUPKG の
identity・サイズ・hash を照合します。結果は作業ディレクトリの `verified-release-artifacts.json` に保存されます。
検証後に再ビルド・再パッケージせず、同じ bytes を公開します。これは静的な配布物検証で、アプリや MSI を起動する検証ではありません。

配信元は R2 バケット `shisui-updates` の `https://shisui.kagayoi.com` です。
channel は `win`、manifest は `releases.win.json`、移行用の固定ファイルは `Shisui-win.msi` です。
GitHub Releases は配信に使いません。
公開は配信中 metadata の退避、版付き nupkg、固定名バイナリ、更新 metadata の順です。
最初のアップロード前に、版付き payload の正確な R2 key を認証済み GET で照合します。
404 のときだけ新規送信し、既存 bytes の SHA256 が一致すれば再送を省略します。不一致や照会不能では公開を停止し、同じ版を異なる内容で上書きしません。
固定名ファイルは、R2アップロード後にCloudflare cache purgeを要求し、APIの受理を確認します。
metadata 公開後の失敗では退避 metadata を復元して再 purge します。
旧ドメインからの更新を維持するため、既存の版付き配布物と旧 Setup.exe は削除せず保持します。
新しい PerUser Setup.exe は公開対象から除外します。
署名認証と実行コマンドは [AGENTS.md](../../AGENTS.md#auto-update--release) を参照してください。

関連文書: [設定の保存とログ](data-model.md)、[DESIGN.md](../../DESIGN.md)
