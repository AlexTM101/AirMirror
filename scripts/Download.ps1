# Keep the cache usable after interrupted requests and checksum failures.
function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory=$true)][string[]]$Urls,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][string]$ExpectedHash,
        [ValidateSet('SHA256','SHA512')][string]$Algorithm = 'SHA256'
    )
    $ErrorActionPreference = 'Stop'
    if ((Test-Path -LiteralPath $Destination -PathType Leaf) -and
        (Get-FileHash -LiteralPath $Destination -Algorithm $Algorithm).Hash -ieq $ExpectedHash) { return }
    $directory = Split-Path ([IO.Path]::GetFullPath($Destination)) -Parent
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $lastFailure = 'No download URL supplied'
    foreach ($url in $Urls) {
        $temporary = Join-Path $directory ([IO.Path]::GetFileName($Destination) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $temporary -TimeoutSec 90
            if ((Get-FileHash -LiteralPath $temporary -Algorithm $Algorithm).Hash -ine $ExpectedHash) {
                throw "$Algorithm checksum mismatch"
            }
            Move-Item -LiteralPath $temporary -Destination $Destination -Force
            return
        } catch {
            $lastFailure = $_.Exception.Message
        } finally {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        }
    }
    throw "Could not download verified $([IO.Path]::GetFileName($Destination)): $lastFailure"
}
