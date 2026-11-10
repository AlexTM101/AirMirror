# Rebuild the embedded native payload from its pinned, verified upstream release.
$ErrorActionPreference='Stop'
$project=$PSScriptRoot
$workspace=Split-Path $project -Parent
$upstream=Join-Path $workspace 'work\upstream'
$bundle=Join-Path $workspace 'work\bundle'
New-Item -ItemType Directory -Force -Path $bundle | Out-Null
# Download only the pinned official release; validate before extracting/running.
$releaseZip = Join-Path $upstream 'engine.zip'
$expected = '5f1792a96efffe84b220edfa7286fa7670806226aa4d0b4b6c7e33ddd7747bf9'
if (-not (Test-Path -LiteralPath $releaseZip)) {
    New-Item -ItemType Directory -Force -Path $upstream | Out-Null
    Invoke-WebRequest 'https://github.com/moieric11/AirPlay-Windows/releases/download/v0.4.1/airplay-windows-v0.4.1-x64.zip' -OutFile $releaseZip
}
if ((Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Upstream checksum mismatch' }
Expand-Archive -LiteralPath $releaseZip -DestinationPath (Join-Path $upstream 'engine') -Force
Copy-Item -Path (Join-Path $upstream 'engine\*') -Destination $bundle -Recurse -Force

# Isolate settings, activation event and mutex from the original application.
# These equal-length UTF-16 substitutions preserve all PE offsets and all code.
$enginePath = Join-Path $bundle 'airplay-windows.exe'
$bytes = [IO.File]::ReadAllBytes($enginePath)
$old = [Text.Encoding]::Unicode.GetBytes('AirPlay-Windows')
$replacement = [Text.Encoding]::Unicode.GetBytes('AirMirror-Local')
if ($old.Length -ne $replacement.Length) { throw 'Unequal binary patch lengths' }
$matches = 0
for ($i = 0; $i -le $bytes.Length - $old.Length; $i++) {
    if ($bytes[$i] -ne $old[0]) { continue }
    $equal = $true
    for ($j = 1; $j -lt $old.Length; $j++) { if ($bytes[$i+$j] -ne $old[$j]) { $equal = $false; break } }
    if ($equal) { [Array]::Copy($replacement,0,$bytes,$i,$replacement.Length); $matches++; $i += $old.Length - 1 }
}
if ($matches -lt 4) { throw "Expected namespace strings missing: $matches" }
# Original native 60 FPS settings bounds are retained.
# Embedded video geometry belongs exclusively to the WinUI shell. The native
# chromeless renderer otherwise snaps/resizes the same child on rotation.
# Locate the PE import thunk for this void SDL function and make it a leaf
# return. The DLL, decoder, scaling and actual orientation remain unchanged.
. (Join-Path $project 'Patch-EmbeddedRenderer.ps1')
$resizeThunks = @(Get-EmbeddedResizeThunk $bytes)
if ($resizeThunks.Count -ne 1 -or $resizeThunks[0].Offset -ne 0xcb1f1 -or $resizeThunks[0].Original -ne 'FF-25-C1-BA-00-00') { throw 'Unexpected SDL resize import thunk' }
$bytes[$resizeThunks[0].Offset] = 0xc3
for ($index=1; $index -lt 6; $index++) { $bytes[$resizeThunks[0].Offset+$index] = 0x90 }
[IO.File]::WriteAllBytes($enginePath,$bytes)
& (Join-Path $project 'Build-Presentation.ps1') -EnginePath $enginePath
& (Join-Path $project 'Build-AirNet.ps1') -EnginePath $enginePath -Destination (Join-Path $bundle 'airnet.dll')
$runtimeLicenses = Join-Path $bundle 'airnet-licenses'
New-Item -ItemType Directory -Force -Path $runtimeLicenses | Out-Null
$zigRoot = Join-Path $workspace 'work\tools\zig-x86_64-windows-0.14.1'
Copy-Item -LiteralPath (Join-Path $zigRoot 'LICENSE') -Destination (Join-Path $runtimeLicenses 'Zig-MIT.txt') -Force
Copy-Item -LiteralPath (Join-Path $zigRoot 'lib\libc\mingw\COPYING') -Destination (Join-Path $runtimeLicenses 'MinGW-w64-COPYING.txt') -Force
Copy-Item -LiteralPath (Join-Path $zigRoot 'lib\libunwind\LICENSE.TXT') -Destination (Join-Path $runtimeLicenses 'LLVM-LICENSE.txt') -Force

# App-local Microsoft VC runtime. No administrator install is needed.
$runtimeNames = @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','msvcp140_codecvt_ids.dll','concrt140.dll')
foreach ($name in $runtimeNames) {
    $dll = Join-Path $env:WINDIR "System32\$name"
    if (Test-Path -LiteralPath $dll) { Copy-Item -LiteralPath $dll -Destination $bundle -Force }
}
Copy-Item -LiteralPath (Join-Path $project 'THIRD_PARTY.md') -Destination $bundle -Force
$packed = Join-Path $project 'engine.zip'
Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $packed -CompressionLevel Optimal -Force
$bundleHash = (Get-FileHash -LiteralPath $packed -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $project 'engine.sha256'),$bundleHash)


Write-Output 'Native payload rebuilt and SHA-256 updated.'
