param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\test-output'), [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $SkipBuild) { & (Join-Path $root 'build-v2.ps1') -OutputDirectory $OutputDirectory }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$reference = Join-Path $OutputDirectory 'OpenCodexLauncher.exe'
$test = Join-Path $OutputDirectory 'RegressionChecks.exe'
& $compiler /nologo /target:exe /platform:x64 ('/win32manifest:' + (Join-Path $root 'app.manifest')) "/out:$test" "/reference:$reference" /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'RegressionChecks.cs') (Join-Path $PSScriptRoot 'ReleaseChecks.cs') (Join-Path $PSScriptRoot 'InstallerChecks.cs') (Join-Path $PSScriptRoot 'UpdaterChecks.cs') (Join-Path $PSScriptRoot 'DesktopDiagnosticChecks.cs') (Join-Path $PSScriptRoot 'DesktopSyncChecks.cs') (Join-Path $PSScriptRoot 'TransportDiagnosticChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination ($test + '.config') -Force
& $test (Join-Path $OutputDirectory ('fixtures-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $OutputDirectory 'regression-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
