# 低遅延を優先する設定方針

おまかせ高速化は、省電力・CPU 負荷・最大受信速度より、通信の待機と損失回復の遅れを減らすことを優先します。Windows 既定値であることだけを採用理由にしません。この方針は Windows の TCP/IP と対応 NIC に適用します。根拠の扱いは[根拠の扱い](conventions.md)、検証結果は[実機での検証結果](testing.md)を参照してください。

| 設定 | おまかせの選択 | 根拠と適用条件 |
| --- | --- | --- |
| ECN | 有効 | 対応する相手・経路で、損失に代えて輻輳通知を扱える。対応ルーターのキュー制御も必要で、単独で待ち行列を解消する機能ではない。[RFC 3168](https://www.rfc-editor.org/rfc/rfc3168.html) |
| timestamps | `allowed` から `enabled` | 送信接続でも交渉する。RTT 計測と PAWS を使えるようにするが、送信待ちを直接短くする設定ではない。[netsh](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-interface)、[RFC 7323](https://www.rfc-editor.org/rfc/rfc7323.html) |
| Fast Open / fallback | 明示的に有効 | 対応アプリ・サーバーでは、初期データ付き接続の開始を最大 1 RTT 節約できる。アプリと相手の対応が必要。fallback は互換性のため有効にする。[RFC 7413](https://www.rfc-editor.org/rfc/rfc7413.html) |
| 割り込み調整・EEE・省電力 | 対応する選択 NIC で無効 | 割り込みの待機や低電力状態からの復帰を減らす。CPU 負荷と消費電力の増加を許容する。詳細は[ゲーム向け NIC の詳細設定](section-04.md)と[電源管理](section-05.md)。[Microsoft](https://learn.microsoft.com/en-us/windows-server/networking/technologies/network-subsystem/net-sub-performance-tuning-nics) |
| RSS・LSOv2・チェックサム | 有効を維持 | Microsoft の低遅延向け指針に沿う。[オフロードの根拠](conventions.md#オフロードの根拠) |
| TCP RSC・UDP URO | 無効を維持 | CPU 負荷より低遅延を優先する。URO の改善量は未実測。[オフロードの根拠](conventions.md#オフロードの根拠) |
| RACK/TLP・受信ウィンドウ | 組で有効・Normal を維持 | 損失検出と受信ウィンドウの不足を避ける。Experimental や再送タイマーの短縮が通常の RTT を短くするとは判断しない。[RFC 8985](https://www.rfc-editor.org/rfc/rfc8985.html) |
| 初回再送・SYN 再送 | `initialrto=1000 maxsynretransmissions=4` を 1 コマンドで組にし、Internet テンプレートの値を読み戻す | 接続要求が失われたときの再試行を早める。断念までの時間を保つ理由と手動の既定化は[採用済みの設計判断](section-03.md)。 |

BBR2 は全 5 テンプレート、RACK/TLP は Compat を除く 4 テンプレートへ適用します。UDP は URO/USO だけを既定化し、UDP 全体の reset は行いません。TCP の既定化は不調な旧設定を除く修復工程とし、その後に RSS・ECN・timestamps・Fast Open / fallback の有効化と RSC の無効化を明示的に適用します。手動の既定化には、この上書きを含めません。

以下の値は強制しません。負荷・ドライバー・輻輳制御との組み合わせを確認する必要があり、低遅延に有利な共通値を確定できないためです。RX/TX バッファの一律最小化、RSS キュー数の最大化・固定 CPU 指定、USO の一律停止、HyStart・PRR の新たな強制値は追加しません。RTO は上の表の初回再送・SYN 再送の組だけを例外とし、それ以外の RTO 値は強制しません。

TCP 全体のペーシング（`pacingprofile=always`）と即時 ACK の扱いは[採用済みの設計判断](section-03.md)、読み戻しの条件は[OS コマンドと API の契約](api-reference.md)に記します。ペーシングの個別の既定化は引き続き `default` です。

関連文書: [ネットワーク操作](commands.md)、[DESIGN.md](../../DESIGN.md)
