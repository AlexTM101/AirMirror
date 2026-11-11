param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. (Join-Path $workspace 'scripts\Download.ps1')
. (Join-Path $PSScriptRoot 'Runtime.ps1')
$pinPath = Join-Path $PSScriptRoot 'vc-runtime.json'
$pin = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
$archive = Join-Path $workspace ('work\tools\microsoft-vc-runtime-' + $pin.version + '-x64.vsix')
Get-VerifiedDownload -Urls $pin.url -Destination $archive -ExpectedHash $pin.sha256
$license = Join-Path $PSScriptRoot 'licenses\Microsoft-VC-Runtime-LICENSE.docx'
if ((Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash -ine $pin.licenseSha256) { throw 'Microsoft VC runtime license checksum mismatch' }
Copy-VcRuntime -Archive $archive -Destination $Destination
$notices = Join-Path $Destination 'airnet-licenses'
New-Item -ItemType Directory -Force -Path $notices | Out-Null
Copy-Item -LiteralPath $license -Destination $notices -Force
Copy-Item -LiteralPath $pinPath -Destination (Join-Path $notices 'Microsoft-VC-Runtime-provenance.json') -Force
Write-Output ('Verified app-local Microsoft VC runtime ' + $pin.version + ' and original license')
