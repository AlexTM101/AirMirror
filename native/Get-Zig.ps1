$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$toolsDir = Join-Path $workspace 'work\tools'
$archive = Join-Path $toolsDir 'zig-0.14.1-verified.zip'
$expected = '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c'
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
$valid = (Test-Path -LiteralPath $archive) -and ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected)
if (-not $valid) {
    $urls = @(
        'https://ziglang.freetls.fastly.net/zig-x86_64-windows-0.14.1.zip',
        'https://pkg.hexops.org/zig/zig-x86_64-windows-0.14.1.zip',
        'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip'
    )
    foreach ($url in $urls) {
        & curl.exe --fail --location --silent --show-error --connect-timeout 10 --max-time 90 $url --output $archive
        if ($LASTEXITCODE -eq 0 -and (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected) { $valid = $true; break }
    }
}
if (-not $valid) { throw 'Could not download the official checksum-pinned Zig 0.14.1 archive' }
& tar.exe -xf $archive -C $toolsDir
if ($LASTEXITCODE -ne 0) { throw 'Compiler extraction failed' }
Write-Output 'Portable Zig 0.14.1 is ready under work/tools; no global installation was performed.'
