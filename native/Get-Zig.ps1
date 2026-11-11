$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $workspace 'scripts\Download.ps1')
$toolsDir = Join-Path $workspace 'work\tools'
$archive = Join-Path $toolsDir 'zig-0.14.1-verified.zip'
$expected = '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c'
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
$urls = @(
        'https://ziglang.freetls.fastly.net/zig-x86_64-windows-0.14.1.zip',
        'https://pkg.hexops.org/zig/zig-x86_64-windows-0.14.1.zip',
        'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip'
)
Get-VerifiedDownload -Urls $urls -Destination $archive -ExpectedHash $expected
& tar.exe -xf $archive -C $toolsDir
if ($LASTEXITCODE -ne 0) { throw 'Compiler extraction failed' }
Write-Output 'Portable Zig 0.14.1 is ready under work/tools; no global installation was performed.'
