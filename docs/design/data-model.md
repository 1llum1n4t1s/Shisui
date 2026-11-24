# 設定の保存とログ

`SettingsService` は `AppSettings` を OS 標準のアプリデータ領域へ保存します。保存は semaphore で直列化し、
一時ファイルを書いてから置換します。設定ファイルが未作成の場合だけ、既定値で初回保存を許可します。
不正・null JSON、アクセス拒否などの読込み失敗時は既定値で画面を開きますが、そのサービスインスタンスでの保存を停止し、原本と NIC・Wi-Fi の復元情報を保護します。問題解消後の再起動で読み直します。
DNS 選択の保存失敗は完了済みの OS 設定変更と分けて通知し、更新確認・スキップ設定の保存失敗はメモリ上の値と表示を変更前へ戻します。
Native AOT での実行を保証するため、JSON は
`ShisuiJsonContext` のソース生成メタデータだけを使います。更新 URL と channel は `[JsonIgnore]` の
読み取り専用値であり、利用者の `settings.json` から外部ホストへ差し替えられません。

`AppPaths` は設定とログの保存先を分離します。Windows の設定は `%APPDATA%\Shisui\settings.json`、
ログは `%LOCALAPPDATA%\Shisui\logs` とし、ログを Roaming の同期対象から外します。
他の OS ではアプリデータ領域の `logs` を使います。
`LoggerBootstrap.Initialize` はファイルログを構成する前に、Windows の旧 `%APPDATA%\Shisui\logs` と
`%LOCALAPPDATA%\Kagayoi\Shisui\logs` から、直下の `.log` ファイルだけを移します。
`LegacyLogMigration` は名前付き mutex を待機なしで取得し、移行元・先と親階層の再解析ポイント、
リンクされたファイルを辿りません。同名ファイルは一意な名前へ変更し、既存ログを上書きしません。
書込み中・アクセス不能などで移せなかったファイルは元に残し、次回起動時に再試行します。
移行元のログフォルダーは空の場合だけ削除し、旧 Kagayoi 配下ではその直上の `Shisui` も空の場合だけ削除します。
Roaming の設定フォルダーと、ログ以外の残存ファイルは保護します。

## 記録と保存の項目

- ViewModel は従来の簡潔な画面ログを維持し、通知を実行 ID で関連付けます。適用後確認などの合成結果は、成功・失敗を問わず両方の出力をファイルへ記録します。
- AppSettings には、NIC の変更前の値、Wi-Fi 電源プランの変更前の AC 値（`WifiPowerPlanSnapshots`）、上り速度の制限の回線ごとのプロファイル（`UplinkLimitProfiles`）を保存します。おまかせの再確認待ち、起動指紋、再起動必須フラグも同じ保存先に置きます。

関連文書: [更新と配布](release-process.md)、[DESIGN.md](../../DESIGN.md)
