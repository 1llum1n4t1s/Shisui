# 自動更新とリリースの制約

配布と更新の設計は [DESIGN.md](../../DESIGN.md#設定と更新) にある。依存パッケージの正式なバージョンは `src/Shisui.UI/Shisui.UI.csproj` と `src/Shisui.Core/Shisui.Core.csproj`、リリース CLI の固定版は `scripts/release-local.ps1` にある。説明文書のバージョン番号ではなく、これらのファイルを参照する。

- 直接参照の `Velopack` パッケージと `$VpkVersion` の CLI 固定版は揃えて保つ。どちらも最新版の自動解決ではない。ダイアログのパッケージは単独で更新してよい。更新・パッケージの変更はすべて `pwsh scripts/release-local.ps1 -SkipUpload` で検証する（win-x64 Native AOT、MSI Directory 正規化、再署名、署名検査を含む）。publish と一致する明示的な `vpk pack --runtime $runtime` を維持する。
- 署名は Sigilune（`scripts/Sigilune.Common.ps1`。Certum のクラウド鍵を signtool の `/dlib` 経由で使用。署名者は `CN=Open Source Developer Yuichiro Shinozaki`）と Windows SDK の `signtool.exe` で行う。SimplySign Desktop は使わない。MSI Directory 正規化後の再署名は `sigilune sign --replace` を使う。
- `.github/workflows/dependency-validation.yml`（build + `dotnet test` + R2 fixtures）は、`main` への push、すべての PR、手動実行、および `release.yml` の `test` ジョブとして動く。`release` ジョブは `needs: test` なので、テストが失敗すると署名と配信が止まる。
- 正式なリリースは `.github/workflows/release.yml` で動く（`main` の commit への軽量タグ `v<version>` の push が契機。Environment は `release`、`SIGILUNE_EMAIL` / `SIGILUNE_TOTP_URI` / `CLOUDFLARE_API_TOKEN`）。ローカル実行には `sigilune setup` が必要。
- `-SkipUpload` はクラウドへの書き込みなしでビルドと署名だけを行う。アップロードには、さらに `CLOUDFLARE_API_TOKEN`、またはローカルではリリーススクリプトが設定した秘密情報の取得元の `cloudflare.api_token` が要る。
- リリーススクリプトは、クリーンな作業ツリー（未追跡ファイルを含む）、.NET 11 SDK、HEAD と一致する完全なソース commit SHA を要求する。`-SourceGitSha` の既定は HEAD で、別のリビジョンは選べない。これらの検査は `-SkipUpload` にも適用される。
- 署名後の自動検査 `scripts/verify-release-artifacts.ps1` と、リリース作業ディレクトリの `verified-release-artifacts.json` の証跡を維持する。検証済みの同じバイト列を、再ビルド・再パックなしで公開する。検証の範囲とアップロードの安全策は DESIGN にある。この静的な成果物検査は UI 検証の代わりにならない。
- リリースワークフロー、または Sigilune のログイン事前確認とローカルのリリーススクリプトは、[release-process.md](release-process.md) のとおりに実行する。バージョンは `/vava` だけで変更し、`Directory.Build.props` の `<Version>` を手で編集しない。
- 署名済みの PerMachine MSI を公開し、生成される PerUser の Setup.exe は公開しない。公開の順序・ロールバック・キャッシュ消去・保持期間は DESIGN にある。
- `Shisui.UI.exe` とそのアセンブリ名は変えない。変えると `avares://Shisui.UI/...` のリソースが壊れる。ショートカットと表示名は `--packTitle` により `Shisui` になる。
- 旧 `shisui.nephilim.jp` の更新エンドポイントは 2027/05/31 まで維持する。リダイレクトは `/` だけにし、更新マニフェストとパッケージは移行期間中も直接ダウンロードできる状態を保つ。
