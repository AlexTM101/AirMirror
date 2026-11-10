function Read-PeImage([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $pe = [BitConverter]::ToInt32($bytes,0x3c)
    if ([BitConverter]::ToUInt32($bytes,$pe) -ne 0x4550) { throw 'Invalid PE image' }
    $optional = $pe+24
    if ([BitConverter]::ToUInt16($bytes,$optional) -ne 0x20b) { throw 'Expected x64 PE image' }
    $sectionCount = [BitConverter]::ToUInt16($bytes,$pe+6)
    $sectionStart = $optional+[BitConverter]::ToUInt16($bytes,$pe+20)
    $sections = @()
    for ($i=0; $i -lt $sectionCount; $i++) {
        $pos=$sectionStart+$i*40
        $sections += @{ Rva=[BitConverter]::ToUInt32($bytes,$pos+12); Size=[BitConverter]::ToUInt32($bytes,$pos+16); Raw=[BitConverter]::ToUInt32($bytes,$pos+20) }
    }
    return @{ Bytes=$bytes; Optional=$optional; Sections=$sections }
}
function Get-PeRaw($image,[uint32]$rva) {
    foreach ($section in $image.Sections) {
        if ($rva -ge $section.Rva -and $rva -lt $section.Rva+$section.Size) { return [int]($section.Raw+$rva-$section.Rva) }
    }
    throw 'PE RVA is outside sections'
}
function Get-PeAscii($image,[int]$offset) {
    $end=$offset
    while ($end -lt $image.Bytes.Length -and $image.Bytes[$end] -ne 0) { $end++ }
    return [Text.Encoding]::ASCII.GetString($image.Bytes,$offset,$end-$offset)
}
function Get-PeExports($image) {
    $exportRva = [BitConverter]::ToUInt32($image.Bytes,$image.Optional+112)
    if ($exportRva -eq 0) { return @() }
    $directory = Get-PeRaw $image $exportRva
    $ordinalBase = [BitConverter]::ToUInt32($image.Bytes,$directory+16)
    $nameCount = [BitConverter]::ToUInt32($image.Bytes,$directory+24)
    $addresses = Get-PeRaw $image ([BitConverter]::ToUInt32($image.Bytes,$directory+28))
    $names = Get-PeRaw $image ([BitConverter]::ToUInt32($image.Bytes,$directory+32))
    $ordinals = Get-PeRaw $image ([BitConverter]::ToUInt32($image.Bytes,$directory+36))
    $result=@()
    for ($i=0; $i -lt $nameCount; $i++) {
        $name=Get-PeAscii $image (Get-PeRaw $image ([BitConverter]::ToUInt32($image.Bytes,$names+$i*4)))
        $index=[BitConverter]::ToUInt16($image.Bytes,$ordinals+$i*2)
        $result += @{ Name=$name; Ordinal=[int]($ordinalBase+$index); Rva=[BitConverter]::ToUInt32($image.Bytes,$addresses+$index*4) }
    }
    return $result
}
function Get-PeImports($image,[string]$moduleName) {
    $directory=Get-PeRaw $image ([BitConverter]::ToUInt32($image.Bytes,$image.Optional+120))
    $result=@()
    for ($pos=$directory; [BitConverter]::ToUInt32($image.Bytes,$pos+12) -ne 0; $pos+=20) {
        $moduleOffset=Get-PeRaw $image ([BitConverter]::ToUInt32($image.Bytes,$pos+12))
        $module=Get-PeAscii $image $moduleOffset
        if ($moduleName -and $module -ine $moduleName) { continue }
        $lookupRva=[BitConverter]::ToUInt32($image.Bytes,$pos)
        if ($lookupRva -eq 0) { $lookupRva=[BitConverter]::ToUInt32($image.Bytes,$pos+16) }
        $lookup=Get-PeRaw $image $lookupRva
        for ($i=0; ; $i++) {
            $entry=[BitConverter]::ToUInt64($image.Bytes,$lookup+$i*8)
            if ($entry -eq 0) { break }
            $name=$null; $ordinal=0
            if (($entry -shr 63) -ne 0) { $ordinal=[int]($entry -band 0xffff) }
            else { $name=Get-PeAscii $image ((Get-PeRaw $image ([uint32]$entry))+2) }
            $result += @{ Module=$module; ModuleOffset=$moduleOffset; Name=$name; Ordinal=$ordinal }
        }
    }
    return $result
}
