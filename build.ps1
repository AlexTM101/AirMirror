param(
    [string]$DotNetPath,
    [switch]$BuildOnly,
    [switch]$RebuildNative
)
$ErrorActionPreference='Stop'
$repoRoot=$PSScriptRoot
$project=Join-Path $repoRoot 'src\AirMirror.WinUI\AirMirror.WinUI.csproj'
$expected=(Get-Content -LiteralPath (Join-Path $repoRoot 'native\engine.sha256') -Raw).Trim()
$payload=Join-Path $repoRoot 'native\engine.zip'
if($RebuildNative){
    & (Join-Path $repoRoot 'native\Get-Zig.ps1')
    & (Join-Path $repoRoot 'native\Get-Iced.ps1')
    & (Join-Path $repoRoot 'native\build.ps1')
    $expected=(Get-Content -LiteralPath (Join-Path $repoRoot 'native\engine.sha256') -Raw).Trim()
}
if((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ine $expected){throw 'Native payload checksum mismatch'}
if(-not $DotNetPath){
    $DotNetPath=Join-Path $repoRoot 'work\tools\dotnet\dotnet.exe'
    if(-not(Test-Path -LiteralPath $DotNetPath)){& (Join-Path $repoRoot 'scripts\Get-Tools.ps1')}
}
$env:DOTNET_ROOT=Split-Path ([IO.Path]::GetFullPath($DotNetPath)) -Parent
$env:DOTNET_CLI_HOME=Join-Path $repoRoot 'work\dotnet-home'
if(-not $env:NUGET_PACKAGES){$env:NUGET_PACKAGES=Join-Path $repoRoot 'work\nuget'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
Push-Location $repoRoot
try {
    $sdk=& $DotNetPath --version
    if($LASTEXITCODE -ne 0 -or $sdk.Trim() -ne '8.0.428'){throw 'Use .NET SDK 8.0.428 (see global.json)'}
    $restore=@('restore',$project,'--verbosity','minimal')
    if(Test-Path -LiteralPath (Join-Path $repoRoot 'src\AirMirror.WinUI\packages.lock.json')){$restore+='--locked-mode'}else{$restore+='--use-lock-file'}
    & $DotNetPath @restore
    if($LASTEXITCODE -ne 0){throw 'Dependency restore failed'}
    if($BuildOnly){
        & $DotNetPath build $project -c Release --no-restore --verbosity minimal
    }else{
        $published=Join-Path $repoRoot 'work\publish'
        & $DotNetPath publish $project -c Release --no-restore -o $published --verbosity minimal
    }
    if($LASTEXITCODE -ne 0){throw 'WinUI 3 compilation failed'}
    if(-not $BuildOnly){
        $version=([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
        $name="AirMirror-$version-WinUI3.exe"
        $dist=Join-Path $repoRoot 'dist'
        New-Item -ItemType Directory -Force -Path $dist | Out-Null
        $target=Join-Path $dist $name
        Copy-Item -LiteralPath (Join-Path $published 'AirMirror.WinUI.exe') -Destination $target -Force
        $hash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'),"$hash  $name`n",[Text.UTF8Encoding]::new($false))
        Write-Output "Portable executable: $target"
    }
}finally{Pop-Location}
