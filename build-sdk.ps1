param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin\sdk'), [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
& $Dotnet build (Join-Path $PSScriptRoot 'OpenCodexLauncher.csproj') -c Release -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'SDK build failed.' }
