param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$DataDirectory,
    [switch]$Recovery
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$DataDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DataDirectory)
New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
$settingsFiles = @(
    (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'AirMirror-Local/settings.ini'),
    (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'AirPlay-Windows/settings.ini')
)
$saved = @{}
foreach ($path in $settingsFiles) {
    $saved[$path] = if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllBytes($path) } else { $null }
}
$oldData = $env:AIRMIRROR_DATA_DIR
$process = $null
try {
    $env:AIRMIRROR_DATA_DIR = $DataDirectory
    $report = Join-Path $DataDirectory 'self-test.txt'
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $argument = if ($Recovery) { '--self-test-recovery' } else { '--self-test' }
    $process = Start-Process -FilePath $Executable -ArgumentList $argument -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw 'AirMirror integration test timed out.' }
    $process.Refresh()
    Write-Output "ExitCode=$($process.ExitCode)"
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report) -or (Get-Content -LiteralPath $report -First 1) -ne 'PASS') {
        Get-ChildItem -LiteralPath $DataDirectory -Filter '*error.txt' | ForEach-Object { Get-Content -LiteralPath $_.FullName }
        throw 'AirMirror integration verification failed.'
    }
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    foreach ($path in $settingsFiles) {
        if ($null -ne $saved[$path]) { [IO.File]::WriteAllBytes($path, $saved[$path]) }
        elseif (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    $env:AIRMIRROR_DATA_DIR = $oldData
}
