# ローカルリリースの実行

通常は GitHub Actions で実行する（本人の PC は不要）。`/vava` が main を push したあと、その commit に軽量タグ `v<version>` を付けて push すると、`release.yml` が自動で起動する。`workflow_dispatch` は同じタグを指定した再実行にだけ使う（`skip_upload=true` でビルド・署名・検証だけを行い、成果物を Actions に残せる）。タグ以外（main など）を指定した実行は、`release.yml` の guard job（タグであること・`v<版>` の形・軽量タグが同じ commit を指すこと・main の祖先であること・`Directory.Build.props` の版との一致）で拒否される。GitHub Release は作らない。

```powershell
gh workflow run release.yml --repo 1llum1n4t1s/Shisui --ref v<version> -f skip_upload=false
```

ローカルで実行する場合は、リポジトリルートで Sigilune のクラウドログインを事前確認し、成功した場合だけ実行する。失敗時は「Sigilune でクラウドにログインできなかったよ。sigilune status で登録を確認してから /vava を再実行してね」。ログインを自動再試行せず、署名を省略して続行しない。

```powershell
$sigilune = if ($env:SIGILUNE_EXE) { $env:SIGILUNE_EXE } else { Join-Path $env:USERPROFILE '.local\Sigilune\bin\sigilune.exe' }
& $sigilune login
if ($LASTEXITCODE -eq 0) { pwsh -NoProfile -File scripts/release-local.ps1 } else { throw 'Sigilune でクラウドにログインできなかったよ。sigilune status で登録を確認してから /vava を再実行してね' }
```
