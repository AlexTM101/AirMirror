function Get-EmbeddedResizeThunk([byte[]]$image) {
    $pe = [BitConverter]::ToInt32($image,0x3c)
    if ([BitConverter]::ToUInt32($image,$pe) -ne 0x4550) { throw 'Invalid PE signature' }
    $sectionCount = [BitConverter]::ToUInt16($image,$pe+6)
    $optionalSize = [BitConverter]::ToUInt16($image,$pe+20)
    $optional = $pe+24
    if ([BitConverter]::ToUInt16($image,$optional) -ne 0x20b) { throw 'Expected PE32+ executable' }
    $sections = @()
    for ($i=0; $i -lt $sectionCount; $i++) {
        $pos = $optional+$optionalSize+$i*40
        $sections += @{
            Name = [Text.Encoding]::ASCII.GetString($image,$pos,8).Trim([char]0)
            Rva = [BitConverter]::ToUInt32($image,$pos+12)
            Size = [BitConverter]::ToUInt32($image,$pos+16)
            Raw = [BitConverter]::ToUInt32($image,$pos+20)
        }
    }
    function Get-RawOffset([uint32]$rva) {
        foreach ($section in $sections) {
            if ($rva -ge $section.Rva -and $rva -lt $section.Rva+$section.Size) { return [int]($section.Raw+$rva-$section.Rva) }
        }
        throw ('RVA outside sections: 0x{0:x}' -f $rva)
    }
    function Get-Ascii([int]$offset) {
        $end = $offset
        while ($end -lt $image.Length -and $image[$end] -ne 0) { $end++ }
        return [Text.Encoding]::ASCII.GetString($image,$offset,$end-$offset)
    }
    $imports = Get-RawOffset ([BitConverter]::ToUInt32($image,$optional+120))
    $resizeIat = 0
    for ($descriptor=$imports; [BitConverter]::ToUInt32($image,$descriptor+12) -ne 0; $descriptor+=20) {
        $module = Get-Ascii (Get-RawOffset ([BitConverter]::ToUInt32($image,$descriptor+12)))
        if ($module -ine 'SDL2.dll') { continue }
        $lookupRva = [BitConverter]::ToUInt32($image,$descriptor)
        $iatRva = [BitConverter]::ToUInt32($image,$descriptor+16)
        if ($lookupRva -eq 0) { $lookupRva = $iatRva }
        $lookup = Get-RawOffset $lookupRva
        for ($index=0; ; $index++) {
            $entry = [BitConverter]::ToUInt64($image,$lookup+$index*8)
            if ($entry -eq 0) { break }
            if (($entry -shr 63) -ne 0) { continue }
            $function = Get-Ascii ((Get-RawOffset ([uint32]$entry))+2)
            if ($function -eq 'SDL_SetWindowSize') { $resizeIat = $iatRva+$index*8; break }
        }
    }
    if ($resizeIat -eq 0) { throw 'SDL_SetWindowSize import missing' }
    $code = @($sections | Where-Object { $_.Name -eq '.text' })
    if ($code.Count -ne 1) { throw 'Ambiguous code section' }
    $found = @()
    for ($offset=[int]$code[0].Raw; $offset -lt $code[0].Raw+$code[0].Size-6; $offset++) {
        if ($image[$offset] -ne 0xff -or $image[$offset+1] -ne 0x25) { continue }
        $instructionRva = [long]$code[0].Rva+$offset-$code[0].Raw
        $target = $instructionRva+6+[BitConverter]::ToInt32($image,$offset+2)
        if ($target -eq $resizeIat) {
            $found += @{ Offset=$offset; Original=([BitConverter]::ToString($image,$offset,6)); Target=$target }
        }
    }
    return $found
}
