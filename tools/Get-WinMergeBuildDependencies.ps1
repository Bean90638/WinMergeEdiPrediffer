[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
$destination = Join-Path $sourceRoot 'Build\msys2'
$sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
if (!$sevenZip) { throw 'WinMerge 建置產生器需要 7-Zip，請先安裝或將 7z 加入 PATH。' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$decoderDirectory = Join-Path $destination 'zstd'
$decoder = Get-ChildItem $decoderDirectory -Filter zstd.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (!$decoder) {
    $decoderArchive = Join-Path $destination 'zstd-v1.5.2-win64.zip'
    Invoke-WebRequest 'https://github.com/facebook/zstd/releases/download/v1.5.2/zstd-v1.5.2-win64.zip' -OutFile $decoderArchive
    Expand-Archive -LiteralPath $decoderArchive -DestinationPath $decoderDirectory -Force
    $decoder = Get-ChildItem $decoderDirectory -Filter zstd.exe -Recurse | Select-Object -First 1
}
if (!$decoder) { throw '找不到 zstd 解壓工具。' }
# 版本取自 WinMerge v2.16.58.2 DownloadDeps.cmd；僅取詞法／語法產生器。
$packages = @('msys2-runtime-3.2.0-14-i686.pkg.tar.zst', 'gcc-libs-13.3.0-1-i686.pkg.tar.zst',
    'lemon-3.46.1-1-i686.pkg.tar.zst', 're2c-3.1-2-i686.pkg.tar.zst')
$manifest = @()
foreach ($filename in $packages) {
    $archive = Join-Path $destination $filename
    $url = 'https://mirror.msys2.org/msys/i686/' + $filename
    # 完整重新下載，避免把上次中斷留下的檔案當作有效套件。
    Invoke-WebRequest $url -OutFile $archive
    $tar = $archive.Substring(0, $archive.Length - 4)
    & $decoder.FullName -d -f $archive -o $tar
    if ($LASTEXITCODE -ne 0) { throw "解開 $filename 失敗。" }
    & $sevenZip.Source x $tar -y ('-o' + $destination) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "展開 $filename 失敗。" }
    $manifest += [ordered]@{ url=$url; sha256=(Get-FileHash $archive -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $destination 'download-info.json') -Encoding utf8
& (Join-Path $destination 'usr\bin\re2c.exe') --version
if ($LASTEXITCODE -ne 0) { throw 're2c 執行驗證失敗。' }
if (!(Test-Path -LiteralPath (Join-Path $destination 'usr\share\lemon\lempar.c'))) { throw '缺少 lemon 範本。' }
Write-Output "已取得 WinMerge 建置產生器：$destination"
