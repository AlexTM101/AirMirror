param([string]$DotNetPath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if(-not $DotNetPath){
    $localDotNet=Join-Path $repoRoot 'work\tools\dotnet\dotnet.exe'
    if(Test-Path -LiteralPath $localDotNet){$DotNetPath=$localDotNet}
    else{$DotNetPath=(Get-Command dotnet -ErrorAction Stop).Source}
}
if(Test-Path -LiteralPath $DotNetPath){$DotNetPath=(Resolve-Path -LiteralPath $DotNetPath).Path}
else{$DotNetPath=(Get-Command $DotNetPath -ErrorAction Stop).Source}
$env:DOTNET_CLI_HOME=Join-Path $repoRoot 'work\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
if(-not $env:NUGET_PACKAGES){$env:NUGET_PACKAGES=Join-Path $repoRoot 'work\nuget'}
$project=Join-Path $PSScriptRoot 'EngineRegression\EngineRegression.csproj'
$source=Join-Path $repoRoot 'src\AirMirror.WinUI\Engine.cs'
$assembly=Join-Path $PSScriptRoot 'EngineRegression\bin\Release\net8.0\EngineRegression.dll'
$cases=@(
    @{Name='mismatched embedded hash';Hash=(Join-Path $PSScriptRoot 'EngineRegression\wrong.sha256');Arguments=@('--bad-hash')},
    @{Name='normal payload and receiver';Hash=(Join-Path $repoRoot 'native\engine.sha256');Arguments=@()}
)
$failed=$false
Push-Location $repoRoot
try{
    foreach($case in $cases){
        Write-Output "Checking $($case.Name)"
        # Changing embedded resource paths can fool incremental compilation.
        # Recompile both modes, finishing with the normal embedded payload.
        $build=@('build',$project,'--configuration','Release','--no-incremental','--verbosity','minimal',
            "-p:EngineSourceFile=$source","-p:EngineHashFile=$($case.Hash)")
        & $DotNetPath @build
        if($LASTEXITCODE -ne 0){$failed=$true;continue}
        # Invoke the DLL deliberately: its child-process regression must also
        # work when launched by dotnet instead of the generated apphost.
        $run=@($assembly)+$case.Arguments
        & $DotNetPath @run
        if($LASTEXITCODE -ne 0){$failed=$true}
    }
}finally{Pop-Location}
if($failed){throw 'Engine regression checks failed'}
