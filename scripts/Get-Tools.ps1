$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$tools=Join-Path $repoRoot 'work\tools'
$sdkRoot=Join-Path $tools 'dotnet'
$sdkZip=Join-Path $tools 'dotnet-sdk-8.0.428-win-x64.zip'
$expected='cce1dc5cfb2c5b73ddd9d3658f6009deb85be3a58dbec44a6dfe3910bb32b044ad2fc6bb28c77aded70c73270a204d207fe790dcb82ffca6c7b42c1335b51c21'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
if(-not(Test-Path -LiteralPath $sdkZip)){
    Invoke-WebRequest 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.428/dotnet-sdk-8.0.428-win-x64.zip' -OutFile $sdkZip
}
if((Get-FileHash -LiteralPath $sdkZip -Algorithm SHA512).Hash.ToLowerInvariant() -ne $expected){throw '.NET SDK checksum mismatch'}
New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
Expand-Archive -LiteralPath $sdkZip -DestinationPath $sdkRoot -Force
Write-Output 'Portable .NET SDK ready in work/tools/dotnet.'
