# Offline behavioral regressions. Downloads are replaced only at the HTTP boundary.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $repoRoot ('work\build-script-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$downloadSupport = Join-Path $PSScriptRoot 'Download.ps1'
if (-not (Test-Path -LiteralPath $downloadSupport)) { throw 'FAIL: verified downloads cannot repair a corrupt cache or isolate interrupted downloads yet' }
. $downloadSupport
$script:payload = [Text.Encoding]::UTF8.GetBytes('hello')
$script:transport = 'success'
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, [int]$TimeoutSec, [switch]$UseBasicParsing)
    if (-not $UseBasicParsing) { throw 'Downloads must explicitly disable response DOM parsing' }
    if ($script:transport -eq 'forbidden') { throw 'Unexpected network access for a verified cache' }
    if ($script:transport -eq 'interrupted') {
        [IO.File]::WriteAllText($OutFile, 'partial')
        throw 'Simulated connection interruption'
    }
    if ($script:transport -eq 'corrupt') { [IO.File]::WriteAllText($OutFile, 'bad response'); return }
    if ($Uri -eq 'https://fixture.invalid/bad') { [IO.File]::WriteAllText($OutFile, 'bad mirror'); return }
    [IO.File]::WriteAllBytes($OutFile, $script:payload)
}
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw ('FAIL: ' + $message) } }
function Fetch([string]$target, [string[]]$urls = @('https://fixture.invalid/good')) {
    Get-VerifiedDownload -Urls $urls -Destination $target -ExpectedHash '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824'
}
function Assert-NoPartialFiles {
    Assert (@(Get-ChildItem -LiteralPath $testRoot -File -Filter '*.tmp').Count -eq 0) 'partial download files were left behind'
}
$target = Join-Path $testRoot 'archive with spaces.zip'
[IO.File]::WriteAllText($target, 'poisoned cache')
Fetch $target
Assert ([IO.File]::ReadAllText($target) -ceq 'hello') 'a corrupt cached archive was not replaced by verified bytes'
Write-Output 'PASS: corrupt cache is repaired, including paths with spaces'

$script:transport = 'forbidden'
Fetch $target
Write-Output 'PASS: verified cache is reused without network access'

$interrupted = Join-Path $testRoot 'interrupted.zip'
$script:transport = 'interrupted'
$failed = $false
try { Fetch $interrupted } catch { $failed = $true }
Assert $failed 'interrupted download was accepted'
Assert (-not (Test-Path -LiteralPath $interrupted)) 'partial bytes were promoted to the final archive'
Assert-NoPartialFiles
$script:transport = 'success'
Fetch $interrupted
Assert ([IO.File]::ReadAllText($interrupted) -ceq 'hello') 'retry after interruption did not recover automatically'
Write-Output 'PASS: interruption leaves no final archive or temporary file and retry recovers'

$bad = Join-Path $testRoot 'bad-checksum.zip'
$script:transport = 'corrupt'
$failed = $false
try { Fetch $bad } catch { $failed = $true }
Assert $failed 'invalid downloaded checksum was accepted'
Assert (-not (Test-Path -LiteralPath $bad)) 'unverified bytes were promoted to the final archive'
Assert-NoPartialFiles
Write-Output 'PASS: checksum failure never publishes downloaded bytes'

$script:transport = 'success'
Fetch (Join-Path $testRoot 'mirrors.zip') @('https://fixture.invalid/bad','https://fixture.invalid/good')
Assert ([IO.File]::ReadAllText((Join-Path $testRoot 'mirrors.zip')) -ceq 'hello') 'valid fallback mirror was not used'
Assert-NoPartialFiles
Write-Output 'PASS: invalid mirror response falls back to verified mirror'

$sha512 = Join-Path $testRoot 'sdk-sha512.zip'
Get-VerifiedDownload -Urls 'https://fixture.invalid/good' -Destination $sha512 -Algorithm SHA512 -ExpectedHash '9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca72323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043'
Assert ([IO.File]::ReadAllText($sha512) -ceq 'hello') 'SHA-512 SDK downloads did not verify and publish'
Write-Output 'PASS: SHA-512 downloads use the same verified cache behavior'

$runtimeSupport = Join-Path $repoRoot 'native\Runtime.ps1'
if (-not (Test-Path -LiteralPath $runtimeSupport)) { throw 'FAIL: native rebuild cannot require a complete app-local runtime yet' }
. $runtimeSupport
Add-Type -AssemblyName System.IO.Compression.FileSystem, System.IO.Compression
$runtimeArchive = Join-Path $testRoot 'runtime.zip'
$runtimeNames = @('vcruntime140.dll','vcruntime140_1.dll','vcruntime140_threads.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','msvcp140_codecvt_ids.dll','concrt140.dll')
$zip = [IO.Compression.ZipFile]::Open($runtimeArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $runtimeNames | Where-Object { $_ -ne 'msvcp140.dll' }) {
        $entry = $zip.CreateEntry('Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.CRT/' + $name)
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('pinned ' + $name) } finally { $writer.Dispose() }
    }
} finally { $zip.Dispose() }
$destination = Join-Path $testRoot 'native bundle'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
[IO.File]::WriteAllText((Join-Path $destination 'vcruntime140.dll'), 'previous runtime')
$failed = $false
try { Copy-VcRuntime -Archive $runtimeArchive -Destination $destination } catch { $failed = $true }
Assert $failed 'runtime archive missing a required DLL was accepted'
Assert ([IO.File]::ReadAllText((Join-Path $destination 'vcruntime140.dll')) -ceq 'previous runtime') 'failed runtime validation partly modified the bundle'
Write-Output 'PASS: missing required runtime DLL fails before changing the bundle'

$zip = [IO.Compression.ZipFile]::Open($runtimeArchive, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.CreateEntry('Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.CRT/msvcp140.dll')
    $writer = [IO.StreamWriter]::new($entry.Open())
    try { $writer.Write('pinned msvcp140.dll') } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
Copy-VcRuntime -Archive $runtimeArchive -Destination $destination
foreach ($name in $runtimeNames) { Assert ([IO.File]::ReadAllText((Join-Path $destination $name)) -ceq ('pinned ' + $name)) ('runtime did not come from the archive: ' + $name) }
Write-Output 'PASS: complete runtime comes entirely from the pinned archive'
Write-Output 'All build-script regressions passed (8 checks).'
