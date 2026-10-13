[CmdletBinding()]
param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '..\work\upstream\winmerge'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\WinMergePathRouting'),
    [string]$WindowsSdkDirectory,
    [string]$MfcDirectory,
    [switch]$FetchSource
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$patchPath = Join-Path $repositoryRoot 'patches\winmerge-v2.16.58.2-original-path.patch'
$expectedCommit = '5f689ef907ec427192bb17d120f0dbb24f1e213b'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw '需要 Visual Studio 2022 C++ x64 建置工具。' }
$msvc = Get-ChildItem (Join-Path $visualStudio 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (!$MfcDirectory) { $MfcDirectory = Join-Path $msvc.FullName 'atlmfc' }
$MfcDirectory = [IO.Path]::GetFullPath($MfcDirectory)
if (!(Test-Path -LiteralPath (Join-Path $MfcDirectory 'include\afxwin.h'))) { throw '缺少 C++ MFC x64 建置配套，請指定 -MfcDirectory 或安裝 Visual Studio 的 C++ MFC 元件。' }
if (!$WindowsSdkDirectory) {
    $kits = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue
    $WindowsSdkDirectory = $kits.KitsRoot10
}
if (!$WindowsSdkDirectory -or !(Test-Path -LiteralPath (Join-Path $WindowsSdkDirectory 'Include'))) { throw '缺少 Windows SDK，請指定 -WindowsSdkDirectory 或安裝 Windows 10/11 SDK。' }
$sdkVersion = Get-ChildItem (Join-Path $WindowsSdkDirectory 'Include') -Directory | Sort-Object Name -Descending | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'um\Windows.h')
} | Select-Object -First 1
if (!$sdkVersion) { throw 'Windows SDK 目錄缺少 Windows.h。' }
$sdkRoot = [IO.Path]::GetFullPath($WindowsSdkDirectory).TrimEnd('\') + '\'
$version = $sdkVersion.Name
if (!(Test-Path -LiteralPath (Join-Path $sdkRoot "Lib\$version\um\x64\kernel32.lib"))) { throw 'Windows SDK 缺少 x64 程式庫。' }
if (!(Test-Path -LiteralPath $sourceRoot)) {
    if (!$FetchSource) { throw '找不到 WinMerge 原始碼；首次建置請加上 -FetchSource。' }
    & git clone --depth 1 --branch v2.16.58.2 https://github.com/WinMerge/winmerge.git $sourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'WinMerge 原始碼下載失敗。' }
}
$head = & git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $head -ne $expectedCommit) { throw '原始碼必須是官方 v2.16.58.2 的指定版本，避免將修補套用到不相容版本。' }
& git -C $sourceRoot apply --reverse --check $patchPath 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $sourceRoot apply --check $patchPath
    if ($LASTEXITCODE -ne 0) { throw '原始碼與修補不一致，未更動原始碼。' }
    & git -C $sourceRoot apply $patchPath
    if ($LASTEXITCODE -ne 0) { throw '套用 WinMerge 修補失敗。' }
}
if ($FetchSource) {
    & git -C $sourceRoot submodule update --init --depth 1 Externals/wil Externals/md4c Externals/darkmodelib Externals/winimerge Externals/winwebdiff Externals/frhed
    if ($LASTEXITCODE -ne 0) { throw '取得 WinMerge 原始碼依賴失敗。' }
}
if ($FetchSource) {
    & (Join-Path $PSScriptRoot 'Get-WinMergeBuildDependencies.ps1') -SourceDirectory $sourceRoot
}
foreach ($generator in @('usr\bin\re2c.exe', 'usr\bin\lemon.exe', 'usr\share\lemon\lempar.c')) {
    if (!(Test-Path -LiteralPath (Join-Path $sourceRoot ('Build\msys2\' + $generator)))) {
        throw '缺少 WinMerge 詞法／語法產生器，請以 -FetchSource 補齊建置依賴。'
    }
}
# 與官方 Version.in 相同的版本，另標示本地原始路徑介面。
@('#define FILEVER 2,16,58,2', '#define PRODUCTVER 2,16,58,2',
  '#define STRFILEVER "2.16.58.2"', '#define STRPRODUCTVER "2.16.58.2"',
  '#define STRPRIVATEBUILD "EDI Original Path API 1"', '#define STRYEARMONTH "2026.08"') |
    Set-Content (Join-Path $sourceRoot 'Version.h') -Encoding ascii
$previousEnvironment = @{}
foreach ($name in @('Path','INCLUDE','LIB')) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $buildPath = (Join-Path $msvc.FullName 'bin\Hostx64\x64') + ';' + (Join-Path $sdkRoot "bin\$version\x64") + ';' + $env:Path
    [Environment]::SetEnvironmentVariable('PATH', $null)
    [Environment]::SetEnvironmentVariable('Path', $buildPath)
    $env:INCLUDE = @((Join-Path $msvc.FullName 'include'), (Join-Path $MfcDirectory 'include'), (Join-Path $msvc.FullName 'atlmfc\include'),
        (Join-Path $sdkRoot "Include\$version\ucrt"), (Join-Path $sdkRoot "Include\$version\shared"),
        (Join-Path $sdkRoot "Include\$version\um"), (Join-Path $sdkRoot "Include\$version\winrt"),
        (Join-Path $sdkRoot "Include\$version\cppwinrt")) -join ';'
    $env:LIB = @((Join-Path $msvc.FullName 'lib\x64'), (Join-Path $MfcDirectory 'lib\x64'), (Join-Path $msvc.FullName 'atlmfc\lib\x64'),
        (Join-Path $sdkRoot "Lib\$version\ucrt\x64"), (Join-Path $sdkRoot "Lib\$version\um\x64")) -join ';'
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $msbuild = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'
    $buildArguments = @((Join-Path $sourceRoot 'Src\Merge.vcxproj'), '/t:Build', '/m', '/nologo', '/v:minimal',
        ('/p:SolutionDir=' + $sourceRoot.TrimEnd('\') + '\'), '/p:Configuration=Release', '/p:Platform=x64', '/p:PlatformToolset=v143', '/p:UseEnv=true',
        "/p:WindowsTargetPlatformVersion=$version", "/p:WindowsSdkDir=$sdkRoot", "/p:WindowsSdkDir_10=$sdkRoot",
        "/p:UniversalCRTSdkDir=$sdkRoot", "/p:UniversalCRTSdkDir_10=$sdkRoot", "/p:UCRTVersion=$version")
    $start = [Diagnostics.ProcessStartInfo]::new($msbuild)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $buildArguments) { $start.ArgumentList.Add($argument) }
    # Windows 環境名稱不分大小寫；避免來自啟動器的 Path/PATH 重複影響原生建置工具。
    $normalized = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $normalized[$entry.Key] = $entry.Value }
    $normalized['PATH'] = $buildPath
    $normalized['INCLUDE'] = $env:INCLUDE
    $normalized['LIB'] = $env:LIB
    $start.Environment.Clear()
    foreach ($entry in $normalized.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    $process = [Diagnostics.Process]::Start($start)
    $errors = $process.StandardError.ReadToEndAsync()
    $writer = [IO.StreamWriter]::new((Join-Path $outputRoot 'build.log'), $false)
    try {
        while (($line = $process.StandardOutput.ReadLine()) -ne $null) { $writer.WriteLine($line); Write-Output $line }
        $process.WaitForExit()
        $errorText = $errors.GetAwaiter().GetResult()
        if ($errorText) { $writer.Write($errorText); Write-Output $errorText }
        if ($process.ExitCode -ne 0) { throw 'WinMerge 建置失敗，請查看輸出資料夾的 build.log。' }
    }
    finally { $writer.Dispose(); $process.Dispose() }
    $builtExecutable = Join-Path $sourceRoot 'Build\x64\Release\WinMergeU.exe'
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($builtExecutable)
    if ($info.PrivateBuild -ne 'EDI Original Path API 1') { throw '建置檔缺少原始路徑介面版本標記。' }
    Copy-Item -LiteralPath $builtExecutable -Destination (Join-Path $outputRoot 'WinMergeU.exe') -Force
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE.md') -Destination (Join-Path $outputRoot 'WinMerge-COPYING.txt') -Force
    Copy-Item -LiteralPath $patchPath -Destination $outputRoot -Force
    [ordered]@{ upstreamCommit=$expectedCommit; originalPathApi=1; executableSha256=(Get-FileHash $builtExecutable -Algorithm SHA256).Hash } |
        ConvertTo-Json | Set-Content (Join-Path $outputRoot 'build-info.json') -Encoding utf8
    Write-Output "已建置 WinMerge 修正版：$outputRoot"
}
finally {
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
}
