# 目的と配布範囲

Shisui は、DNS 設定、通信診断、ネットワークアダプター情報、Windows の TCP・メンテナンス操作を
一つの Avalonia デスクトップ UI から扱うアプリです。コードは Windows と macOS を対象に
`net11.0` で構成されていますが、署名・インストーラー・自動更新を含む正式な配布経路は Windows
win-x64 のみです。macOS 実装はコンパイルとパーサー単体テストまでで、実機検証と署名・notarization は行われていません。

関連文書: [DESIGN.md](../../DESIGN.md)、[AGENTS.md](../../AGENTS.md)、[README.md](../../README.md)
