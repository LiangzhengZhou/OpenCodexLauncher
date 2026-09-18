param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$assembly = [Reflection.Assembly]::LoadFile($exe)
if ($assembly.EntryPoint.DeclaringType.FullName -ne 'OpenCodexLauncherV2.AppEntry') {
    throw 'Not the Launcher GUI entry point. A wrapper must not be distributed as the Launcher.'
}
$profile = Join-Path ([IO.Path]::GetTempPath()) ('launcher-startup-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profile | Out-Null
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $exe
$start.Arguments = '--isolated "' + $profile + '"'
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$process = [Diagnostics.Process]::Start($start)
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw ('GUI exited before creating a window: ' + $process.ExitCode) }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or $process.MainWindowTitle -notlike 'OpenCodex Launcher *') {
        throw 'Launcher main window did not appear.'
    }
    Write-Output ('PASS: actual GUI entry point opens a Launcher window: ' + $process.MainWindowTitle)
    if (!$process.CloseMainWindow() -or !$process.WaitForExit(10000)) { throw 'Isolated Launcher did not close cleanly.' }
    if ($process.ExitCode -ne 0) { throw 'Launcher exited unsuccessfully.' }
    Write-Output 'PASS: isolated Launcher closes cleanly; no personal settings or services touched.'
} finally {
    # Only the process created by this check is eligible for cleanup.
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
