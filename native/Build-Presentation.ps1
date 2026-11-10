param([string]$EnginePath)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$toolsDir=Join-Path $workspace 'work\tools'
$dependency=Join-Path $toolsDir 'Iced.dll'
if (-not (Test-Path -LiteralPath $dependency)) { throw 'Iced 1.21.0 build dependency missing; see SOURCE-README.md' }
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /utf8output "/reference:$dependency" "/out:$toolsDir\PresentationPatcher.exe" (Join-Path $PSScriptRoot 'PresentationPatcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Presentation patcher compilation failed' }
& (Join-Path $toolsDir 'PresentationPatcher.exe') $EnginePath (Join-Path $PSScriptRoot 'presentation-layout.h')
if ($LASTEXITCODE -ne 0) { throw 'Presentation bridge patch failed' }
