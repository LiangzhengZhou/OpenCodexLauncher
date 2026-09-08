param([string]$OutputDirectory = (Split-Path -Parent $MyInvocation.MyCommand.Path), [string]$OutputName = 'OpenCodexLauncher.exe')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'LauncherV2.cs'
$core = Join-Path $root 'LauncherCore.cs'
$icon = Join-Path $root 'assets\launcher.ico'
$iconPng = Join-Path $root 'assets\launcher.png'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory $OutputName
$compileDirectory = Join-Path $OutputDirectory ('build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $compileDirectory | Out-Null
$compileOutput = Join-Path $compileDirectory $OutputName
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $csc)) { throw 'Windows C# compiler csc.exe was not found.' }
$framework = Split-Path $csc
$refs = @(
  (Join-Path $framework 'System.dll'),
  (Join-Path $framework 'System.Core.dll'),
  (Join-Path $framework 'System.IO.Compression.dll'),
  (Join-Path $framework 'System.IO.Compression.FileSystem.dll'),
  (Join-Path $framework 'System.Data.dll'),
  (Join-Path $framework 'System.Drawing.dll'),
  (Join-Path $framework 'System.Net.Http.dll'),
  (Join-Path $framework 'System.Management.dll'),
  (Join-Path $framework 'System.Security.dll'),
  (Join-Path $framework 'System.Web.Extensions.dll'),
  (Join-Path $framework 'WPF\WindowsBase.dll'),
  (Join-Path $framework 'WPF\PresentationCore.dll'),
  (Join-Path $framework 'WPF\PresentationFramework.dll'),
  (Join-Path $framework 'System.Xaml.dll')
)
$cscArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + $compileOutput))
if (-not (Test-Path -LiteralPath $icon)) { throw 'Missing assets/launcher.ico.' }
$cscArgs += ('/win32icon:' + $icon)
$cscArgs += ('/win32manifest:' + (Join-Path $root 'app.manifest'))
$cscArgs += ('/resource:' + $iconPng + ',OpenCodexLauncher.icon.png')
foreach ($ref in $refs) { $cscArgs += ('/reference:' + $ref) }
$cscArgs += (Get-ChildItem -LiteralPath $root -Filter *.cs | ForEach-Object { $_.FullName })
$cscArgs += ('/resource:' + (Join-Path $root 'localization.json') + ',OpenCodexLauncher.localization.json')
& $csc @cscArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $compileOutput)) { throw 'WPF compilation failed.' }
try { Move-Item -LiteralPath $compileOutput -Destination $output -Force; Write-Output ('Created: ' + $output) }
catch { throw "Cannot replace the executable. Close the launcher and rebuild. Compiled file retained at $compileOutput" }
Remove-Item -LiteralPath $compileDirectory
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination ($output + '.config') -Force
