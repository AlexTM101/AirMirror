function Copy-VcRuntime {
    param(
        [Parameter(Mandatory=$true)][string]$Archive,
        [Parameter(Mandatory=$true)][string]$Destination
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem, System.IO.Compression
    $names = @('vcruntime140.dll','vcruntime140_1.dll','vcruntime140_threads.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','msvcp140_codecvt_ids.dll','concrt140.dll')
    $prefix = 'Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.CRT/'
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        # Validate the complete release-only set before changing the bundle.
        $entries = @()
        foreach ($name in $names) {
            $found = @($zip.Entries | Where-Object { $_.FullName -ceq ($prefix + $name) })
            if ($found.Count -ne 1 -or $found[0].Length -eq 0) { throw "Required pinned VC runtime DLL missing or ambiguous: $name" }
            $entries += $found[0]
        }
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
        foreach ($entry in $entries) {
            $source = $entry.Open()
            try {
                $target = [IO.File]::Create((Join-Path $Destination $entry.Name))
                try { $source.CopyTo($target) } finally { $target.Dispose() }
            } finally { $source.Dispose() }
        }
    } finally { $zip.Dispose() }
}
