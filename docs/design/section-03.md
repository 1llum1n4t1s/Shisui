# 採用済みの設計判断

- **Core/UI/Tests の分離**: OS コマンドの組み立てと解析を UI から外し、実機を変更せずに大半をテストできます。
  その代わり、ViewModel はユースケースの順序と排他境界を明示的に管理します。
- **プラットフォーム中立な `net11.0`**: 一つのモデルと UI を共有しつつ、OS 固有機能を DI で差し替えます。
  Windows 専用機能は macOS で stub を返さず UI ごと隠すため、利用可能範囲が明確になります。
- **Native AOT**: JIT を含まない単独配布と起動経路を採用しています。反面、reflection 依存を避け、
  JSON source generation と実配布条件での publish 検証が必要です。
- **サイドメニューの画像リソース**: Microsoft Fluent UI System Icons の公式カラー版を PNG として埋め込み、
  アイコンフォントや実行時の SVG ライブラリを追加せずに表示します。原本 SVG と出典を保持し、
  MIT ライセンスをビルド・配布出力へ同梱します。素材の対応と出典は
  [リソースの説明](../../src/Shisui.UI/Resources/FluentSystemIcons/README.md)を参照してください。
- **ロケール非依存の機械可読出力**: 表示用コマンドの文字列解析よりコマンド数や builder/parser が増えますが、
  日本語・英語 Windows と console encoding の差で状態判定が壊れることを避けます。
- **実行時昇格**: `requireAdministrator` manifest ではなく Velopack フック後の自己昇格を選び、
  インストーラー互換性と一度だけの UAC を両立します。
- **CI 署名リリース**: Sigilune が SimplySign Desktop なしで Certum のクラウド鍵を使えるため、main の commit に付けた軽量タグ `v<版>` の push で起動する
  `release.yml` が `release-local.ps1` を実行して署名・配信します。再現可能な固定 CLI と
  検証・ロールバックを含む `release-local.ps1` に配布責務を集約し、ローカル実行も同じ手順で動きます。
  TOTP 登録情報を持つ Secrets は Environment `release` のジョブだけが読みます。

- **TCP のペーシング `always`**: おまかせは PC 全体の `pacingprofile` を `always` にします。媒体ごとに分けず、送信を平滑化してキュー圧を抑える機構を優先します。BBR2 は必須条件にしません。有線での比較では中央値の一貫した改善を確認できなかったため、遅延改善を保証する設定とは説明しません。
- **即時 ACK（`TcpAckFrequency=1`）**: 旧 ACK/Nagle 調整の除去後、選択 NIC の既存インターフェースキーにだけ `TcpAckFrequency` を DWORD の 1 で設定し、DWORD 型・値・NIC の GUID を読み戻して確認します。上りが細い回線では即時 ACK が上りを圧迫し得ます。この 1 台の試算では、下り 90〜95Mbps 中の送信（ほぼ ACK）が 3.5〜4.7Mbps で、上り 10Mbps 未満の回線なら約 35〜47% に当たります。この観測は即時 ACK と既定の ACK の比較ではなく、ACK 頻度の A/B は未実施です。そこで、選択 NIC が現在の既定経路の回線で、その回線の計測が 30 日以内かつ上り容量 10Mbps 未満のときだけ設定を省き、除去後の Windows 既定のままにします（`UplinkAckPolicy`）。回線・計測を確認できない場合は従来どおり 1 にします。おまかせから計測・制限は起動しません。接続名整理より前に適用し、整理後は個別設定の一覧と選択対象も更新します。個別設定として、値 1 の適用、設定値の表示、値の削除による既定への復帰を設けます。変更後は再起動が必要で、設定値の表示だけでは再起動後の実動作を保証しません。`TcpDelAckTicks` の旧説明や MSMQ 固有の `TCPNoDelay` は一般アプリへ転用しません。テンプレートの DelayedAckFrequency / DelayedAckTimeoutMs は変更せず、インターフェースの値だけを操作します。関連仕様は [KB328890](https://learn.microsoft.com/en-us/troubleshoot/windows-server/networking/registry-entry-control-tcp-acknowledgment-behavior) と [Set-NetTCPSetting](https://learn.microsoft.com/en-us/powershell/module/nettcpip/set-nettcpsetting) です。

関連文書: [DESIGN.md](../../DESIGN.md)
