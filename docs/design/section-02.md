# 重要な不変条件

- `ICommandExecutor` は配列ではなく整形済み引数文字列を受け取ります。`netsh` が生のコマンドラインを
  独自解析するため、adapter 名の `name="..."` を .NET の `ArgumentList` で再エスケープしません。
- DNS アドレスはコマンド生成前に IPv4/IPv6 として検証し、文字列引数へ埋め込む値の引用符を拒否します。
- PowerShell スクリプトへ値を埋め込むときは、単一引用符リテラルの生成を `WindowsPowerShellLiteral` に集約します。
  PowerShell は ASCII の `'` に加えて U+2018・U+2019・U+201A・U+201B も単一引用符として扱うため、
  これらを各文字自身で二重化し、値中の引用符でリテラルが閉じて後続がコードとして評価されることを防ぎます。
- PowerShell でアダプター名を扱うときは、`Get-NetAdapter -Name` のワイルドカード解釈へ直接渡さず、
  全アダプターから大文字小文字を区別しない完全一致で一意に解決します。後続 cmdlet の `-Name` へ渡す場合も
  ワイルドカードをエスケープし、意図しない複数アダプターの読み取り・変更を防ぎます。
- Windows の現在値はローカライズされた `netsh` 表示を解析せず、PowerShell で固定した `KEY=VALUE`
  または XML を解析します。標準出力は raw byte を同時に読み、厳密 UTF-8、次に OEM code page の順で復号します。
  Fast Open は `netsh interface tcp dump` の機械可読な `set global` 行から一意な `fastopen=enabled/disabled` を取得し、
  PowerShell で `FASTOPEN=Enabled/Disabled` へ変換します。行・値の重複、未知値、コマンド失敗は未確認とします。
  例外として RACK/TLP の変更不要判定だけは、PowerShell に値がないため確認済みの日英ラベルを限定解析します。
  未知の言語・不完全な出力を有効と推測せず、設定コマンドの失敗を成功へ置き換えません。
- Windows の外部コマンドは既知のシステムコマンドだけを絶対パスへ解決し、未知の相対実行ファイルを拒否します。
- executor のキャンセル時と開始後の例外時は子プロセスツリー全体へ終了を要求して待機します。
  終了要求が拒否されても実際の終了まで待機し、呼び出し元の処理中状態と変更操作の排他を保持します。
  同期ストリームで出力を読む経路では残りの標準出力・標準エラーを破棄しながら読み続け、パイプ詰まりを避けます。
  この終了待機はキャンセル対象外なので、各 service のタイムアウトは処理が戻るまでの上限ではありません。
  IP 初期化や条件付き修復の中断結果に対する後続操作停止・再起動案内は、各 service と呼び出し元が管理します。
- builder、catalog、parser は OS を呼ばない純粋処理です。OS アクセスは service、executor、専用のネイティブ API 実装の責務です。
- `INetworkMutationGate` は非再入です。複合操作は外側で一度だけ取得し、内側から再取得しません。
- 公式 DNS プリセットの IP、DoH template、DoT host は一体の契約です。特に Cloudflare のフィルタ段階ごとの
  hostname を標準 hostname へ置き換えません。NextDNS とカスタム設定には固定の暗号化 DNS endpoint を与えません。
- DoH のチェック状態は OS の実状態から復元します。DoT はロケール非依存に状態取得できる API がないため、
  保存状態を実状態として表示せず、操作時だけ有効化・無効化します。
- DoH は対象 IP の登録を一括取得し、欠落だけを追加、不整合なテンプレート・自動昇格だけを修復します。既存の UDP fallback 禁止は維持します。正しい登録は再設定しません。グローバル有効化の終了値と、対象の期待 HTTPS テンプレート・型付き bool・fallback の読み戻しを確認し、成功しない設定を表示上だけ有効にしません。登録値確認と実際の TLS 通信成功は区別します。
- 上り速度の制限は ActiveStore の `ShisuiUplinkLimit` / `ShisuiUplinkTrial` だけを扱い、`Shisui*` 以外の QoS ポリシーを変更・削除しません。試験用と適用用を同時に存在させず、計測後は試験用の残存 0 件を読み戻します。おまかせ高速化設定から計測・制限を起動しません。
- Windows の正式配布物は署名済み PerMachine MSI です。新しい PerUser `Setup.exe` は公開せず、旧配布物は[更新と配布](release-process.md)の保持方針に従います。旧 PerUser 版を
  ユーザー書き込み可能な場所から管理者実行し続けません。移行・修復対象は検証済みの既知パスに限定します。
  ダウンロードした MSI は書換え・削除を拒否する同一ハンドルで署名検証し、そのハンドルから再解析ポイントを解決した最終パスだけを `msiexec` へ渡して終了まで保持します。
- 自動更新元は `https://shisui.kagayoi.com` の `releases.win.json` です。旧
  `shisui.nephilim.jp` は出荷済みクライアントのため期限まで配信を維持します。

関連文書: [DESIGN.md](../../DESIGN.md)
