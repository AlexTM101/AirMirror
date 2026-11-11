$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Download.ps1')
$tools=Join-Path $repoRoot 'work\tools'
$sdkRoot=Join-Path $tools 'dotnet'
$sdkZip=Join-Path $tools 'dotnet-sdk-8.0.428-win-x64.zip'
$expected='cce1dc5cfb2c5b73ddd9d3658f6009deb85be3a58dbec44a6dfe3910bb32b044ad2fc6bb28c77aded70c73270a204d207fe790dcb82ffca6c7b42c1335b51c21'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
Get-VerifiedDownload -Urls 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.428/dotnet-sdk-8.0.428-win-x64.zip' -Destination $sdkZip -ExpectedHash $expected -Algorithm SHA512
New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
Expand-Archive -LiteralPath $sdkZip -DestinationPath $sdkRoot -Force
Write-Output 'Portable .NET SDK ready in work/tools/dotnet.'
