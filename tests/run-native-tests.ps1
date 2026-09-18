param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $OutputDirectory 'OpenCodexLauncher.exe'
$test = Join-Path $OutputDirectory 'NativeChecks.exe'
& $compiler /nologo /target:exe ('/out:' + $test) ('/reference:' + $exe) /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'NativeChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed' }
Copy-Item (Join-Path $root 'App.config') ($test + '.config')
& $test (Join-Path $OutputDirectory ('native-' + [Guid]::NewGuid().ToString('N').Substring(0,8)))
if ($LASTEXITCODE -ne 0) { throw 'Native checks failed' }
