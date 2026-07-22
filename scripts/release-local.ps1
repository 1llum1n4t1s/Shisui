# release-local.ps1 — ローカル署名付き Velopack リリース (VStoVSC テンプレートから横展開)
#
# SimplySign (Certum クラウド署名) は Desktop 接続 + スマホトークンが必要で
# GitHub Actions からは署名できないため、リリースは本スクリプトでローカル実行する。
#
# 前提:
#   - SimplySign Desktop が接続済み (証明書が CurrentUser\My に見えていること)
#   - Directory.Build.props の <Version> がリリースしたいバージョンになっていること (/vava 済み)
#   - C:\Users\IMT\dev\Secret\secrets.json に cloudflare.api_token があること
#
# 使い方:
#   pwsh scripts/release-local.ps1                # フルリリース (build + sign + upload + cleanup)
#   pwsh scripts/release-local.ps1 -SkipUpload    # ビルド + 署名のみ (アップロードしない動作確認用)

[CmdletBinding()]
param(
    [switch]$SkipUpload,
    [string[]]$Runtimes = @('win-x64')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- 定数 ----
# リリースの再現性と set-msi-program-files-location.ps1 の前提 (1.2.0 の MSI レイアウト) を保つため、
# vpk はリポジトリ内で明示的に固定する (Lumin4ti と同方式)。
# 更新時は公式 NuGet の安定版を確認し、-SkipUpload で署名成果物を検証してから変更する。
$VpkVersion = '1.2.0'
Write-Host "vpk 固定バージョン: $VpkVersion"
$WranglerVersion = '4.92.0'         # サプライチェーン対策でバージョン固定
$Bucket = 'shisui-updates'
$BaseUrl = 'https://shisui.nephilim.jp'
$AccountId = '10901bfadbf1005164774a7350082985'
$SecretsPath = 'C:\Users\IMT\dev\Secret\secrets.json'
$CertSubjectName = 'Open Source Developer Yuichiro Shinozaki'
# /n (Subject 名) で選択: 証明書の年次更新で thumbprint が変わっても動く
$SignParams = "/n `"$CertSubjectName`" /fd SHA256 /td SHA256 /tr http://time.certum.pl"
$SignToolPath = Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' `
    -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like '*\x64' } |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $SignToolPath) { throw 'Windows SDK x64 signtool.exe が見つかりません' }

# Shisui は win-x64 のみ配信 (ARM64 配信なし)。macOS は Apple 署名/notarization が別途必要なため対象外。
$RuntimeMatrix = @{
    'win-x64' = @{ Channel = 'win' }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot
$WorkDir = Join-Path $RepoRoot 'local-release'
$ArtifactsDir = Join-Path $WorkDir 'artifacts'
$RollbackDir = Join-Path $WorkDir 'rollback-previous'

function Invoke-Native {
    param([string]$Description, [scriptblock]$Block)
    & $Block
    if ($LASTEXITCODE -ne 0) { throw "$Description が失敗しました (exit $LASTEXITCODE)" }
}

# ---- 0. プリフライト ----
Write-Host '== プリフライト ==' -ForegroundColor Cyan

# Git Bash (MSYS) 経由で起動すると括弧入り環境変数が落ちて vswhere.exe 解決等が
# 壊れることがあるため補完する (非 AOT でも実害なしの保険)
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = 'C:\Program Files (x86)' }
$vsInstallerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ($env:PATH -notlike "*$vsInstallerDir*") { $env:PATH = "$env:PATH;$vsInstallerDir" }

# vpk (dotnet tool) は .NET 9 ランタイム要求だがローカルは 10 のみ → ロールフォワード
$env:DOTNET_ROLL_FORWARD = 'Major'

# XPath で取得 (member enumeration は Version を持たない PropertyGroup 混在時に StrictMode で throw する)
$versionNode = ([xml](Get-Content 'Directory.Build.props' -Raw)).SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { $null }
if (-not $version) { throw 'Directory.Build.props から <Version> を取得できませんでした' }
Write-Host "バージョン: $version"

# SimplySign 接続確認 (証明書が見えなければ署名できないので最初に落とす)
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -like "CN=$CertSubjectName*" -and $_.NotAfter -gt (Get-Date) }
if (-not $cert) {
    throw "署名証明書 (CN=$CertSubjectName) が見つかりません。SimplySign Desktop を起動してトークンでログインしてください。"
}
Write-Host "署名証明書: $($cert.Subject) (期限 $($cert.NotAfter.ToString('yyyy-MM-dd')))"

# vpk を固定バージョンで用意
$vpkInstalled = (dotnet tool list --global | Select-String -SimpleMatch 'vpk') -match [regex]::Escape($VpkVersion)
if (-not $vpkInstalled) {
    Write-Host "vpk $VpkVersion をインストールします..."
    dotnet tool uninstall --global vpk 2>$null | Out-Null
    Invoke-Native 'vpk のインストール' { dotnet tool install --global vpk --version $VpkVersion }
}

# Cloudflare トークン (アップロード時のみ必要)
if (-not $SkipUpload) {
    $secrets = Get-Content $SecretsPath -Raw | ConvertFrom-Json
    if (-not $secrets.cloudflare.api_token) { throw "secrets.json に cloudflare.api_token が見つかりません" }
    $env:CLOUDFLARE_API_TOKEN = $secrets.cloudflare.api_token
    $env:CLOUDFLARE_ACCOUNT_ID = $AccountId
}

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

# ---- 1. ビルド + 署名付きパッケージング (RID ごと) ----
foreach ($runtime in $Runtimes) {
    $config = $RuntimeMatrix[$runtime]
    if (-not $config) { throw "未知の runtime: $runtime" }
    $publishDir = Join-Path $WorkDir "publish-$runtime"

    # restore と publish のパラメータを完全一致させる (self-contained を restore にも渡さないと
    # runtime packs が assets.json に入らず NETSDK1112 が出る)。
    Write-Host "== restore: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet restore ($runtime)" {
        dotnet restore src/Shisui.UI/Shisui.UI.csproj -r $runtime --force-evaluate -p:SelfContained=true
    }

    Write-Host "== publish: $runtime ==" -ForegroundColor Cyan
    Invoke-Native "dotnet publish ($runtime)" {
        dotnet publish src/Shisui.UI/Shisui.UI.csproj -c Release -r $runtime `
            --self-contained true --no-restore -o $publishDir
    }

    if (-not (Test-Path (Join-Path $publishDir 'Shisui.UI.exe'))) {
        throw "Shisui.UI.exe が publish 出力にありません ($runtime)"
    }

    Write-Host "== vpk pack + 署名: $runtime ==" -ForegroundColor Cyan
    # StartMenu は packAuthors 名のサブフォルダを作るため、直下登録の StartMenuRoot を使う。
    Invoke-Native "vpk pack ($runtime)" {
        vpk pack `
            --packId Shisui `
            --packVersion $version `
            --packTitle 'Shisui' `
            --packAuthors 'ゆろち' `
            --mainExe Shisui.UI.exe `
            --icon (Join-Path 'src' 'Shisui.UI' 'icon' 'app.ico') `
            --packDir $publishDir `
            --outputDir $ArtifactsDir `
            --channel $config.Channel `
            --shortcuts 'StartMenuRoot,Desktop' `
            --msi `
            --instLocation PerMachine `
            --signParams $SignParams
    }
}

# Velopack 1.2.0 の生成MSIは PerMachine 指定でも INSTALLFOLDER が TARGETDIR 直下になり、
# Windows Installer が C:\Shisui と解決する。Directory表を公式の64-bit Program Files構造へ直し、
# 変更によって無効になったMSI本体の署名を上書きして再署名する。
foreach ($msi in Get-ChildItem $ArtifactsDir -Filter '*.msi' -File) {
    & (Join-Path $PSScriptRoot 'set-msi-program-files-location.ps1') -MsiPath $msi.FullName
    Invoke-Native "Program Files固定MSIの再署名 ($($msi.Name))" {
        & $SignToolPath sign /n $CertSubjectName /fd SHA256 /td SHA256 `
            /tr 'http://time.certum.pl' $msi.FullName
    }
}

# 署名検証 (MSIを含むインストーラが正しく署名されているかリリース前に確認)
Write-Host '== 署名検証 ==' -ForegroundColor Cyan
foreach ($signedArtifact in Get-ChildItem $ArtifactsDir -File | Where-Object { $_.Extension -in '.exe', '.msi' }) {
    $sig = Get-AuthenticodeSignature $signedArtifact.FullName
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike "CN=$CertSubjectName*") {
        throw "署名検証失敗: $($signedArtifact.Name) → $($sig.Status)"
    }
    Write-Host "  ✅ $($signedArtifact.Name): Valid ($($sig.SignerCertificate.Subject -replace ',.*$'))"
}

if ($SkipUpload) {
    Write-Host "`n✅ -SkipUpload 指定のためここで終了。成果物: $ArtifactsDir" -ForegroundColor Green
    Get-ChildItem $ArtifactsDir | Format-Table Name, @{n='Size(MB)'; e={[math]::Round($_.Length/1MB,1)}}
    return
}

# ---- 1.5 現行配信メタデータの退避 ----
# 新しい版を公開した直後に検証が失敗した場合、固定URLのメタデータだけを直前版へ戻せるようにする。
# 直前 manifest が参照する nupkg は後段の cleanup でも保持する。
New-Item -ItemType Directory -Path $RollbackDir -Force | Out-Null
$artifactFiles = @(Get-ChildItem $ArtifactsDir -File)
$legacySetupFiles = @($artifactFiles | Where-Object { $_.Name -like '*-Setup.exe' })
# PerUser Setup.exe は今後公開しない。MSIの伝播確認後にR2上の旧Setupも削除する。
$publishArtifactFiles = @($artifactFiles | Where-Object { $_.Name -notlike '*-Setup.exe' })
$metadataFiles = @($publishArtifactFiles | Where-Object {
    $_.Name -eq 'RELEASES' -or $_.Name -like 'releases.*.json' -or $_.Name -like 'assets.*.json'
} | Sort-Object @{ Expression = {
    # 現行クライアントが読む releases.*.json を更新メタデータの中でも最後に切り替える。
    if ($_.Name -like 'releases.*.json') { 2 } elseif ($_.Name -eq 'RELEASES') { 1 } else { 0 }
} }, Name)
$payloadFiles = @($publishArtifactFiles | Where-Object { $_.Name -like '*.nupkg' })
$fixedBinaryFiles = @($publishArtifactFiles | Where-Object {
    $_.Name -notlike '*.nupkg' -and $_.Name -ne 'RELEASES' -and
    $_.Name -notlike 'releases.*.json' -and $_.Name -notlike 'assets.*.json'
})
$previousManifestAssets = @{}
foreach ($metadata in $metadataFiles) {
    $url = "$BaseUrl/$($metadata.Name)?_=$([Guid]::NewGuid().ToString('N'))"
    try {
        $response = Invoke-WebRequest -Uri $url -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30
        $raw = $response.Content
        if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }
        Set-Content -LiteralPath (Join-Path $RollbackDir $metadata.Name) -Value $raw -NoNewline -Encoding utf8

        if ($metadata.Name -like 'releases.*.json') {
            foreach ($asset in ($raw | ConvertFrom-Json).Assets) {
                if ($asset.FileName) { $previousManifestAssets[$asset.FileName] = $true }
            }
        }
    } catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $statusCode = if ($responseProperty -and $responseProperty.Value) {
            [int]$responseProperty.Value.StatusCode
        } else {
            $null
        }
        if ($statusCode -ne 404) {
            throw "現行配信メタデータの退避に失敗しました: $($metadata.Name) — $($_.Exception.Message)"
        }
    }
}
Write-Host "直前版の退避: $((Get-ChildItem $RollbackDir -File).Count) メタデータ / $($previousManifestAssets.Count) nupkg"

function Publish-R2Artifact {
    param([System.IO.FileInfo]$File)
    Write-Host "  ↑ $($File.Name)"
    Invoke-Native "R2 put ($($File.Name))" {
        pnpm dlx "wrangler@$WranglerVersion" r2 object put "$Bucket/$($File.Name)" --file $File.FullName --remote
    }
}

# ---- 2. R2 アップロード ----
Write-Host '== R2 アップロード ==' -ForegroundColor Cyan
$uploaded = 0
$publishedMetadata = [System.Collections.Generic.List[string]]::new()
$zoneId = $null
try {
    # manifest が先に見えて未アップロードの nupkg を参照しないよう、版付きpayloadを最初に置く。
    foreach ($file in $payloadFiles) {
        Publish-R2Artifact $file
        $uploaded++
    }
    foreach ($file in $fixedBinaryFiles) {
        Publish-R2Artifact $file
        $uploaded++
    }
    # 自動更新クライアントが読む固定URLのメタデータは最後に公開する。
    foreach ($file in $metadataFiles) {
        Publish-R2Artifact $file
        $publishedMetadata.Add($file.Name)
        $uploaded++
    }
    Write-Host "✅ R2 アップロード完了: $uploaded ファイル"

    # ---- 2.5 Cloudflare エッジキャッシュのパージ ----
    # 固定名ファイル (MSI / Portable.zip / RELEASES / releases.*.json / assets.*.json) は
    # 毎リリースで中身が変わるのに URL が不変。パージしないと自動更新が旧版を掴む。
    Write-Host '== Cloudflare キャッシュパージ ==' -ForegroundColor Cyan
    $cfHeaders = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }
    $zoneName = ([uri]$BaseUrl).Host -replace '^[^.]+\.', ''   # <sub>.nephilim.jp → nephilim.jp (apex)
    $zoneResp = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones?name=$zoneName" -Headers $cfHeaders -TimeoutSec 30
    if (-not $zoneResp.success -or @($zoneResp.result).Count -eq 0) { throw "Cloudflare zone '$zoneName' の取得に失敗しました" }
    $zoneId = $zoneResp.result[0].id
    $purgeUrls = @($publishArtifactFiles | Where-Object { $_.Name -notlike '*.nupkg' } | ForEach-Object { "$BaseUrl/$($_.Name)" })
    if ($purgeUrls.Count -gt 0) {
        $purgeBody = "{`"files`":$(ConvertTo-Json -InputObject $purgeUrls -AsArray -Compress)}"
        $purgeResp = Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
            -Headers $cfHeaders -ContentType 'application/json' -Body $purgeBody -TimeoutSec 30
        if (-not $purgeResp.success) { throw "Cloudflare キャッシュパージに失敗しました: $($purgeResp.errors | ConvertTo-Json -Compress)" }
        Write-Host "  ✅ パージ: $($purgeUrls.Count) URL"
    } else {
        Write-Host '  パージ対象なし'
    }

# ---- 3. 配信確認 (manifest 完全一致リトライ方式) ----
# 単純な HTTP 200 だと CDN/edge が古い manifest を返している間に cleanup が走り、旧 manifest を
# 取得したクライアントが直後に消える .nupkg を取りに行って 404 する race がある。
# ローカルアップロード済 manifest と完全一致するまでリトライしてから次へ進む。
    Write-Host '== 配信確認 (manifest 伝播待機) ==' -ForegroundColor Cyan
    foreach ($runtime in $Runtimes) {
    $channel = $RuntimeMatrix[$runtime].Channel
    $url = "$BaseUrl/releases.$channel.json"
    $localManifest = Get-Content (Join-Path $ArtifactsDir "releases.$channel.json") -Raw |
        ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress

    $maxAttempts = 18
    $matched = $false
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        $resp = Invoke-WebRequest -Uri "${url}?_=$([Guid]::NewGuid().ToString('N'))" `
            -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30
        # R2 は text 系でない Content-Type で返すことがあり、その場合 .Content は byte[] になる
        $raw = $resp.Content
        if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }
        $remoteManifest = $raw | ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress
        if ($localManifest -eq $remoteManifest) {
            Write-Host "  ✅ $url がローカル manifest と一致 (attempt $attempt)"
            $matched = $true
            break
        }
        Write-Host "  ⚠️ remote manifest がまだ古い (attempt $attempt / $maxAttempts)、5 秒待機..."
        Start-Sleep -Seconds 5
    }
        if (-not $matched) {
            throw "remote manifest が $($maxAttempts * 5) 秒以内にローカルと一致しませんでした。race 回避のため cleanup を中止します: $url"
        }
    }

    # 旧PerUser版が安全に移行できるよう、固定URLのMSI本体もサイズ一致まで確認する。
    foreach ($msi in $fixedBinaryFiles | Where-Object { $_.Extension -eq '.msi' }) {
        $msiUrl = "$BaseUrl/$($msi.Name)"
        $response = Invoke-WebRequest -Method Head -Uri "${msiUrl}?_=$([Guid]::NewGuid().ToString('N'))" `
            -Headers @{ 'Cache-Control' = 'no-cache' } -TimeoutSec 30
        $remoteLengths = @(
            @($response.Headers.'Content-Length') |
                ForEach-Object { $_ -split ',' } |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ }
        )
        if ($remoteLengths.Count -eq 0) {
            throw "remote MSI の Content-Length を取得できません: $msiUrl"
        }
        foreach ($remoteLengthText in $remoteLengths) {
            $remoteLength = 0L
            if (-not [long]::TryParse(
                    $remoteLengthText,
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$remoteLength) -or
                $remoteLength -ne $msi.Length) {
                throw "remote MSI のサイズがローカルと一致しません: $msiUrl (remote=$remoteLengthText local=$($msi.Length))"
            }
        }
        Write-Host "  ✅ $msiUrl の配信サイズ一致"
    }
} catch {
    $publishError = $_
    $restoredUrls = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $publishedMetadata) {
        $backup = Join-Path $RollbackDir $name
        if (Test-Path $backup) {
            Write-Warning "公開失敗のため直前版へ復元します: $name"
            Invoke-Native "R2 rollback ($name)" {
                pnpm dlx "wrangler@$WranglerVersion" r2 object put "$Bucket/$name" --file $backup --remote
            }
            $restoredUrls.Add("$BaseUrl/$name")
        }
    }

    if ($zoneId -and $restoredUrls.Count -gt 0) {
        $rollbackBody = "{`"files`":$(ConvertTo-Json -InputObject @($restoredUrls) -AsArray -Compress)}"
        Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
            -Headers $cfHeaders -ContentType 'application/json' -Body $rollbackBody -TimeoutSec 30 | Out-Null
    }
    throw $publishError
}

# ---- 4. 旧バージョン nupkg のクリーンアップ (Aggressive 戦略) ----
# ローカル artifacts の manifest (= 今アップロードしたものと同一) から keep set を作り、
# R2 上の「.nupkg かつ manifest 外」だけを削除する。固定ファイル名 (Setup.exe /
# MSI / Portable.zip / RELEASES* / assets.*.json / releases.*.json) は対象外なので安全。
Write-Host '== 旧 nupkg クリーンアップ ==' -ForegroundColor Cyan
$keep = @{}
$manifests = Get-ChildItem $ArtifactsDir -Filter 'releases.*.json'
if (-not $manifests) { throw 'artifacts に releases.*.json が見つかりません' }
foreach ($m in $manifests) {
    foreach ($asset in (Get-Content $m.FullName -Raw | ConvertFrom-Json).Assets) {
        if ($asset.FileName) { $keep[$asset.FileName] = $true }
    }
}
foreach ($assetName in $previousManifestAssets.Keys) {
    $keep[$assetName] = $true
}
Write-Host "  保持対象 nupkg: $($keep.Count) 件 (現行 + 直前版)"

$api = "https://api.cloudflare.com/client/v4/accounts/$AccountId/r2/buckets/$Bucket"
$headers = @{ Authorization = "Bearer $($env:CLOUDFLARE_API_TOKEN)" }

# MSIが配信済みになった後で、再びPerUser版を入れられないよう旧Setup.exeをR2から除去する。
foreach ($legacySetup in $legacySetupFiles) {
    $encoded = [uri]::EscapeDataString($legacySetup.Name)
    try {
        Invoke-RestMethod -Method Delete -Uri "$api/objects/$encoded" -Headers $headers -TimeoutSec 30 | Out-Null
        Write-Host "  🗑️  旧PerUserインストーラ: $($legacySetup.Name)"
    } catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $statusCode = if ($responseProperty -and $responseProperty.Value) { [int]$responseProperty.Value.StatusCode } else { $null }
        if ($statusCode -ne 404) { throw }
    }
}
if ($legacySetupFiles.Count -gt 0) {
    $legacySetupUrls = @($legacySetupFiles | ForEach-Object { "$BaseUrl/$($_.Name)" })
    $legacyPurgeBody = "{`"files`":$(ConvertTo-Json -InputObject $legacySetupUrls -AsArray -Compress)}"
    Invoke-RestMethod -Method Post -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/purge_cache" `
        -Headers $cfHeaders -ContentType 'application/json' -Body $legacyPurgeBody -TimeoutSec 30 | Out-Null
}

$allKeys = [System.Collections.Generic.List[string]]::new()
$cursor = ''
while ($true) {
    $uri = "$api/objects?per_page=1000" + $(if ($cursor) { "&cursor=$cursor" })
    $resp = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 30
    foreach ($obj in $resp.result) { $allKeys.Add($obj.key) }
    $info = $resp.PSObject.Properties['result_info']
    if (-not $info -or -not $info.Value) { break }
    $truncated = $info.Value.PSObject.Properties['is_truncated']
    if (-not $truncated -or -not $truncated.Value) { break }
    $cursorProp = $info.Value.PSObject.Properties['cursor']
    $cursor = if ($cursorProp) { $cursorProp.Value } else { '' }
    if (-not $cursor) { break }
}

$toDelete = $allKeys | Where-Object { $_ -like '*.nupkg' -and -not $keep.ContainsKey($_) }
if (-not $toDelete) {
    Write-Host '  ✅ 削除対象なし'
} else {
    $deleted = 0; $failed = 0
    foreach ($key in $toDelete) {
        $encoded = [uri]::EscapeDataString($key)
        try {
            Invoke-RestMethod -Method Delete -Uri "$api/objects/$encoded" -Headers $headers -TimeoutSec 30 | Out-Null
            Write-Host "  🗑️  $key"
            $deleted++
        } catch {
            Write-Warning "  削除失敗: $key — $($_.Exception.Message)"
            $failed++
        }
    }
    Write-Host "  🧹 クリーンアップ: $deleted 削除 / $failed 失敗"
    if ($failed -gt 0 -and $deleted -eq 0) { throw '旧 nupkg の削除がすべて失敗しました。API token の権限を確認してください。' }
}

# ---- 5. packages.lock.json は未使用 (このプロジェクトは RestorePackagesWithLockFile を使っていない) ----
# 将来 lockfile を導入したら、ここに `dotnet restore <slnx> --force-evaluate` の clean 化ブロックを足す。

Write-Host "`n🎉 リリース完了: v$version → $BaseUrl" -ForegroundColor Green
