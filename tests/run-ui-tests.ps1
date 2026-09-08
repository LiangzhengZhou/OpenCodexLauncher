param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\test-output'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$reference = Join-Path $OutputDirectory 'OpenCodexLauncher.exe'
$test = Join-Path $OutputDirectory 'UiChecks.exe'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler /nologo /target:exe /platform:x64 "/out:$test" "/reference:$reference" @refs (Join-Path $PSScriptRoot 'UiChecks.cs') (Join-Path $PSScriptRoot 'InstallerChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed' }
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'App.config') -Destination ($test + '.config') -Force
& $test (Join-Path $OutputDirectory ('ui-' + [Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $OutputDirectory 'ui-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'UI checks failed' }
