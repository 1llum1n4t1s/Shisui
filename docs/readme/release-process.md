# R2 配布

[README](../../README.md)から移した、配布の補足です。

R2 への配布は PowerShell 7.2 以上と同梱 `scripts/R2Distribution.Common.ps1` を使用します。Node.js・pnpm・Wrangler は不要です。既存の Cloudflare API token を使用し、固定 URL は再検証、不変の版付き成果物は1年間キャッシュします。

署名・配信の手順は[AGENTS.md](../../AGENTS.md#auto-update--release)、設計は[DESIGN.md](../../DESIGN.md#設定と更新)を参照してください。
