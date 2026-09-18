param([Parameter(Mandatory)][string]$OutputDirectory,[string]$ZipPath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$exe=Join-Path $OutputDirectory 'OpenCodexLauncher.exe'
if(-not (Test-Path $exe)){throw 'Build the launcher first.'}
if(-not $ZipPath){
 $ZipPath=Join-Path $OutputDirectory 'milestone-2.6.5.zip'
 if(-not (Test-Path $ZipPath)){Invoke-WebRequest 'https://github.com/LiangzhengZhou/OpenCodexLauncher/releases/download/v2.6.5/OpenCodexLauncher-2.6.5-windows-x64.zip' -OutFile $ZipPath}
}
$ZipPath=[IO.Path]::GetFullPath($ZipPath)
if((Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne '700a5eb7514079dfeefc3671bcd81386022bfb7d7c42795e365654face7dc566'){throw 'Milestone package digest mismatch'}
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$test=Join-Path $OutputDirectory 'RollbackChecks.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$test" "/reference:$exe" /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'RollbackChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Rollback test compilation failed'}
Copy-Item (Join-Path $root 'App.config') ($test+'.config') -Force
& $test $ZipPath (Join-Path $OutputDirectory ('rollback-fixture-'+[Guid]::NewGuid().ToString('N'))) | Tee-Object -FilePath (Join-Path $OutputDirectory 'rollback-results.txt')
if($LASTEXITCODE -ne 0){throw 'Rollback checks failed'}

$zip304=Join-Path $OutputDirectory 'milestone-3.0.4.zip'
if(-not (Test-Path $zip304)){Invoke-WebRequest 'https://github.com/LiangzhengZhou/OpenCodexLauncher/releases/download/v3.0.4/OpenCodexLauncher-3.0.4-windows-x64.zip' -OutFile $zip304}
if((Get-FileHash $zip304 -Algorithm SHA256).Hash.ToLowerInvariant() -ne '57daf27d1f810c9d7d3f548545ea1b6e40bb89a0b053ec2a12e7701c555ea39f'){throw '3.0.4 milestone digest mismatch'}
& $test $zip304 (Join-Path $OutputDirectory ('rollback304-'+[Guid]::NewGuid().ToString('N'))) '3.0.4' | Tee-Object -FilePath (Join-Path $OutputDirectory 'rollback304-results.txt')
if($LASTEXITCODE -ne 0){throw '3.0.4 rollback checks failed'}