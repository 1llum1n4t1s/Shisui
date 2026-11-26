# インストール移行の信頼境界

`WindowsPerMachineMigration` は旧 `%LocalAppData%\Shisui` 版を昇格する前に署名済み MSI へ移行します。
キャンセル時は終了し、ユーザー書込み可能な旧バイナリを管理者実行し続けません。
`ExecutableTrustVerifier` は System32 の WinVerifyTrust を使い、期待する publisher CN とチェーン全体の
失効状態を検証します。ダウンロードした MSI は Online、ローカル実行ファイルは CacheOnly を使い、
証明書は別のファイル読込みではなく検証済み trust chain から取得します。
MSI の同一ハンドルを署名検証から `msiexec` 終了まで保持する条件は[重要な不変条件](critical-invariants.md)に示します。

信頼済み PerMachine 版は旧 `Update.exe` を実行せず、再解析ポイントを辿らない限定的な清掃を行います。
HKLM の安定実行ファイル登録、`.msi-installed`、現在の実行場所と署名 publisher が一致する場合だけ
旧ルートを扱います。MSI を直接入れた場合も、旧ルートに `Update.exe` や `packages` だけが残る場合も
同じ判定を使います。ロックされた項目以外の清掃を進め、pending marker と HKCU RunOnce で残りを再試行します。
pending marker は一時ファイルへ書き切ってから置き換え、書込み途中の中断で内容が壊れにくくします。
読込めない marker は存在しない扱いにして既存の補完経路へ任せ、片付け時は一時ファイルも削除します。
旧実行ファイル・パッケージ・HKCU アンインストール登録・個人用ショートカットを整理し、
`%APPDATA%\Shisui` の設定とログは保持します。既定の旧ルートでは直下の `logs` を清掃対象から除外し、
それだけが残った状態もインストール移行の清掃完了とします。ログの移行は
[設定の保存とログ](data-model.md)で扱い、ログの残存だけで pending marker や RunOnce を維持しません。
管理者指定の独自インストール先は HKLM 登録・marker・署名の一致で受け入れ、勝手に移動しません。

誤配置修復は確認済みの `C:\Shisui` と `Program Files\ゆろち\Shisui` だけを対象にし、
同意後に固定配信元の署名済み MSI で `Program Files\Shisui` へ移します。
MSI 実行前に正確な旧ルートを管理者書込み限定の HKLM marker へ記録し、RunOnce で清掃を引き継ぎます。
同じ ProductCode の保守が何も変更しなければ `REINSTALL=ALL REINSTALLMODE=vamus` で再実行します。

旧 `StartMenu\ゆろち\Shisui.lnk` は更新フックでルートへ移し、通常起動でも再試行します。
既存ルートのショートカットは上書きせず、旧フォルダーは空のときだけ取り除きます。
移動と旧 PerUser 清掃は Programs フォルダーへ再帰的な `SHChangeNotify(SHCNE_UPDATEDIR)` を送り、
Windows Start の重複キャッシュを更新します。

関連文書: [起動](workflow.md)、[DESIGN.md](../../DESIGN.md)
