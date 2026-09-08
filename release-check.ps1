param([Parameter(Mandatory)][string]$Directory, [ValidateSet('source','binary')][string]$Kind = 'source', [string[]]$ForbiddenValues = @())
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scanRoot = [IO.Path]::GetFullPath($Directory).TrimEnd('\','/')
$prefix = $scanRoot + [IO.Path]::DirectorySeparatorChar
$allowed = if ($Kind -eq 'source') { @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-files.txt') | Where-Object { $_.Trim() }) } else { @('OpenCodexLauncher.exe','OpenCodexLauncher.exe.config','README.md','README.zh-CN.md','CHANGELOG.md','RELEASE_NOTES.md','LICENSE','SHA256SUMS.txt') }
$findings = [Collections.Generic.List[string]]::new()
$count = 0
foreach ($entry in Get-ChildItem -LiteralPath $scanRoot -Recurse -Force) {
    if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Release trees cannot contain linked files or directories.' }
}
foreach ($file in Get-ChildItem -LiteralPath $scanRoot -File -Recurse -Force) {
    if (-not $file.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Scan path escaped root.' }
    $relative = $file.FullName.Substring($prefix.Length).Replace('\','/')
    $count++
    if ($relative -notin $allowed) { $findings.Add("$relative : not in release allowlist") }
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $findings.Add("$relative : linked file") }
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $texts = @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes), [Text.Encoding]::BigEndianUnicode.GetString($bytes))
    if ($bytes.Length -gt 1) {
        $texts += [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
        $texts += [Text.Encoding]::BigEndianUnicode.GetString($bytes, 1, $bytes.Length - 1)
    }
    foreach ($text in $texts) {
        if ($text -match '(?i)sk-[A-Za-z0-9_-]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') { $findings.Add("$relative : credential-like value") }
        if ($text -match '(?i)[A-Z]:[\\/](?:Users|Lib|Software)[\\/]') { $findings.Add("$relative : personal absolute path") }
        foreach ($value in $ForbiddenValues) {
            if ($value.Length -ge 4 -and $text.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $findings.Add("$relative : private value match"); break }
        }
    }
}
if ($findings.Count) {
    $findings | Sort-Object -Unique | Write-Output
    throw 'Release privacy check failed. Values are intentionally not printed.'
}
foreach ($required in $allowed) { if (-not (Test-Path -LiteralPath (Join-Path $scanRoot $required) -PathType Leaf)) { throw "Missing release file: $required" } }
Write-Output "PASS: $Kind release scan, $count allowlisted files, UTF-8/UTF-16/binary strings checked."
