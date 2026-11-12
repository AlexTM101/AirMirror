param([string]$Archive = (Join-Path (Split-Path $PSScriptRoot -Parent) 'native\engine.zip'))
# Inspect actual distribution bytes, including engine ZIPs nested in source ZIPs.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem, System.IO.Compression
$script:issues = [Collections.Generic.List[string]]::new()
$script:checked = 0
function Check-Package([IO.Compression.ZipArchive]$zip, [string]$label) {
    foreach ($entry in $zip.Entries) {
        if (-not $entry.Name) { continue }
        $script:checked++
        $name = $label + '!' + $entry.FullName
        if ($entry.Name -match '(?i)\.(pdb|lib|obj|ilk|exp|dmp|log|pem|key|pfx|p12)$' -or
            $entry.Name -match '(?i)^(settings\.ini|first_frame\.ppm|self-test\.txt|startup-trace\.txt|launch-error\.txt|startup-error\.txt|ui-error\.txt|\.env(?:\..*)?)$' -or
            $entry.FullName -match '(?i)(^|/)(\.git|work|bin|obj)(/|$)') {
            $script:issues.Add($name + ': non-distribution or private runtime file')
        }
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            if ($entry.Name -match '(?i)\.(zip|docx)$') {
                $memory.Position = 0
                $nested = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read, $true)
                try { Check-Package $nested $name } finally { $nested.Dispose() }
                continue
            }
            $bytes = $memory.ToArray()
            $ascii = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
            $unicode = [Text.Encoding]::Unicode.GetString($bytes)
            $userPath = '(?i)(?:[A-Z]:[\\/](?:Users|Documents and Settings)[\\/]|/(?:home|Users)/)[^\x00-\x1f"<>|]+'
            if ([regex]::IsMatch($ascii, $userPath) -or [regex]::IsMatch($unicode, $userPath)) {
                $script:issues.Add($name + ': personal build-machine path')
            }
            if ($entry.Name -ieq 'airnet.dll') {
                # RSDS CodeView records contain a 16-byte GUID, a 4-byte age,
                # then a NUL-terminated PDB path. Never print the private path.
                foreach ($record in [regex]::Matches($ascii, 'RSDS')) {
                    $start = $record.Index + 24
                    if ($start -ge $ascii.Length) { continue }
                    $end = $ascii.IndexOf([char]0, $start)
                    if ($end -lt $start -or $end - $start -gt 1024) { continue }
                    $path = $ascii.Substring($start, $end - $start)
                    if ($path -match '^(?:[A-Za-z]:[\\/]|[\\/])') {
                        $script:issues.Add($name + ': absolute CodeView debug path')
                    }
                }
            }
        } finally { $memory.Dispose(); $stream.Dispose() }
    }
}
$archivePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Archive)
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try { Check-Package $zip ([IO.Path]::GetFileName($Archive)) } finally { $zip.Dispose() }
if ($script:issues.Count) { throw ('Package privacy check failed:' + "`n" + ($script:issues -join "`n")) }
Write-Output ('PASS: package privacy, ' + $script:checked + ' files checked, no debug/private artifacts or personal paths')
