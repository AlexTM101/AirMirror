$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$tools=Join-Path $workspace 'work\tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$archive=Join-Path $tools 'iced-1.21.0.zip'
$expected='d31613617e3ddf91239bbc94a8c587849b4236e8f8a2a2ba59ea2397a1487c7a'
if(-not (Test-Path -LiteralPath $archive)){
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/iced/1.21.0/iced.1.21.0.nupkg' -OutFile $archive -TimeoutSec 60
}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'Iced package checksum mismatch'}
$directory=Join-Path $tools 'iced-1.21.0'
Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force
Copy-Item -LiteralPath (Join-Path $directory 'lib\net45\Iced.dll') -Destination (Join-Path $tools 'Iced.dll') -Force
Copy-Item -LiteralPath (Join-Path $directory 'LICENSE.txt') -Destination (Join-Path $tools 'Iced-LICENSE.txt') -Force
Write-Output 'Pinned Iced 1.21.0 build dependency is ready.'
