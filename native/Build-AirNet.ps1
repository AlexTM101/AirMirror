param([string]$EnginePath, [string]$Destination)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Pe-Network.ps1')
$workspace = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $workspace 'work\tools\zig-x86_64-windows-0.14.1\zig.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Pinned Zig 0.14.1 compiler missing; see SOURCE-README.md' }
$image = Read-PeImage $EnginePath
$imports = @(Get-PeImports $image 'WS2_32.dll')
if ($imports.Count -ne 27) { throw 'Unexpected pinned Winsock import set' }
$systemExports = @(Get-PeExports (Read-PeImage (Join-Path $env:WINDIR 'System32\ws2_32.dll')))
$definitions = @('LIBRARY airnet','EXPORTS')
foreach ($import in $imports) {
    if ($import.Name) { $targets = @($systemExports | Where-Object { $_.Name -ceq $import.Name }) }
    else { $targets = @($systemExports | Where-Object { $_.Ordinal -eq $import.Ordinal }) }
    if ($targets.Count -ne 1) { throw 'Winsock import mapping is ambiguous' }
    $name = $targets[0].Name; $ordinal = $targets[0].Ordinal
    if ($name -eq 'accept' -or $name -eq 'recv' -or $name -eq 'closesocket') { $target='am_'+$name }
    else { $target='WS2_32.'+$name }
    $definitions += ('  {0}={1} @{2}' -f $name,$target,$ordinal)
}
$definitions += '  AirNetSelfTest=am_self_test @2000'
$definitions += '  AirNetGetStats=am_stats @2001'
$definitions += '  AM_PushFrame @2002'
$definitions += '  AM_ClearFrames @2003'
$definitions += '  AM_StopFrames @2004'
$definitions += '  AM_DestroyFrames @2005'
$definitions += '  AirVideoSelfTest @2006'
$defPath = Join-Path $PSScriptRoot 'airnet.def'
[IO.File]::WriteAllLines($defPath,$definitions,[Text.Encoding]::ASCII)
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $workspace 'work\tools\zig-global-cache'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $workspace 'work\tools\zig-local-cache'
# Distribution DLLs must not contain source paths or generate private PDBs.
& $compiler cc -target x86_64-windows-gnu -std=c11 -O2 -g0 -s -Wall -Wextra -Werror -shared (Join-Path $PSScriptRoot 'airnet.c') (Join-Path $PSScriptRoot 'airvideo.c') $defPath -lws2_32 -lavrt -o $Destination
if ($LASTEXITCODE -ne 0) { throw 'AirNet build failed' }
$exports = @(Get-PeExports (Read-PeImage $Destination))
foreach ($import in $imports) {
    if ($import.Name) { $mapped = @($exports | Where-Object { $_.Name -ceq $import.Name }) }
    else { $mapped = @($exports | Where-Object { $_.Ordinal -eq $import.Ordinal }) }
    if ($mapped.Count -ne 1) { throw 'AirNet export verification failed' }
}
$offsets = @($imports.ModuleOffset | Select-Object -Unique)
if ($offsets.Count -ne 1) { throw 'Ambiguous Winsock descriptor' }
$replacement = [Text.Encoding]::ASCII.GetBytes('airnet.dll')
[Array]::Copy($replacement,0,$image.Bytes,$offsets[0],$replacement.Length)
[IO.File]::WriteAllBytes($EnginePath,$image.Bytes)
Write-Output 'Verified private AirNet adapter and all 27 Winsock exports'
