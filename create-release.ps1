param([Parameter(Mandatory)][string]$OutputDirectory, [string]$Executable)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $releaseRoot) { throw 'Choose a new release directory so an existing release is not overwritten.' }
New-Item -ItemType Directory -Path $releaseRoot | Out-Null
$sourceDir = Join-Path $releaseRoot 'github-source'
$binaryDir = Join-Path $releaseRoot 'windows-x64'
New-Item -ItemType Directory -Path $sourceDir,$binaryDir | Out-Null
foreach ($relative in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-files.txt')) {
    if (-not $relative.Trim()) { continue }
    if ($relative -match '\.\.|^[\/]|:') { throw 'Invalid release manifest entry.' }
    $from = Join-Path $PSScriptRoot $relative
    $to = Join-Path $sourceDir $relative
    if ((Get-Item -LiteralPath $from).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Release manifest cannot include linked files.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to
}
if (-not $Executable) { & (Join-Path $PSScriptRoot 'build-v2.ps1') -OutputDirectory $binaryDir }
else {
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $binaryDir 'OpenCodexLauncher.exe')
    Copy-Item -LiteralPath ($Executable + '.config') -Destination (Join-Path $binaryDir 'OpenCodexLauncher.exe.config')
}
$exe = Join-Path $binaryDir 'OpenCodexLauncher.exe'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne '3.0.2.0') { throw 'Executable version does not match 3.0.2 stable.' }
foreach ($file in @('README.md','README.zh-CN.md','CHANGELOG.md','RELEASE_NOTES.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $sourceDir $file) -Destination $binaryDir }
$binaryHashes = Get-ChildItem -LiteralPath $binaryDir -File | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $binaryDir 'SHA256SUMS.txt'),$binaryHashes,[Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'release-check.ps1') -Directory $sourceDir -Kind source
& (Join-Path $PSScriptRoot 'release-check.ps1') -Directory $binaryDir -Kind binary
# ZipFile includes dotfiles such as .github and .gitignore; Compress-Archive may omit them.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($sourceDir,(Join-Path $releaseRoot 'OpenCodexLauncher-3.0.2-source.zip'))
[IO.Compression.ZipFile]::CreateFromDirectory($binaryDir,(Join-Path $releaseRoot 'OpenCodexLauncher-3.0.2-windows-x64.zip'))
$hashes = Get-ChildItem -LiteralPath $releaseRoot -Filter *.zip | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Write-Output 'Release created. Publish only github-source or the generated ZIP files.'
