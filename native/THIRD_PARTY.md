# AirMirror 1.5.1 — components and source

AirMirror shell, adapters and modifications are GNU GPL version 3.
Copyright (c) 2026 AirMirror contributors. The full GPL text is included as
LICENSE in the source package and the native payload.

Native engine: AirPlay-Windows v0.4.1, moieric11 and contributors, based on UxPlay.
https://github.com/moieric11/AirPlay-Windows/tree/v0.4.1
The complete tagged source, build/dependency recipe and original notices are
included in AirMirror-source.zip. The engine's LICENSE, documentation and
THIRD_PARTY_NOTICES.md are preserved in the native payload.

## Modifications

1. Equal-length UTF-16 namespace substitution AirPlay-Windows -> AirMirror-Local
isolates settings and single-instance identifiers. For a source build, change
the corresponding wide string literals in settings.cpp and single_instance.h.
2. The SDL_SetWindowSize import thunk is replaced by a void leaf return for
this embedded receiver. The WinUI shell owns child geometry. The thunk is resolved from
imports and its full pinned signature is checked. engine-embedded.patch is the
source equivalent; use git apply --unidiff-zero from the native project root.
3. The single WS2_32.dll import descriptor is rebound to equal-length airnet.dll.
Build-AirNet.ps1 verifies all 27 original names/ordinals. ABI-compatible wrappers
for accept/recv/closesocket configure per-socket ACK frequency and scoped MMCSS;
all other functions are direct system WS2_32 forwarders. airnet.c is GPL-3.0.
4. Four renderer entry bridges connect airvideo.c's bounded playout queue.
PresentationPatcher.cs validates original prologues, relocates whole instructions
and preserves unwind entries in added PE metadata. Data and code sections have
separate RW and RX permissions. Original clear/stop/destructor methods remain
responsible for native lifetime; the worker is synchronized before those calls.
The actual patch and generated presentation-layout.h are supplied in source.
The queue operates on public libavutil 60 AVFrame references and preserves GPU
buffer ownership. Fast mode directly calls the original push method.
5. The experimental native 120 FPS settings patch from old versions is removed.
The original 60 FPS parser upper bounds are retained.

Original release ZIP SHA-256 before modifications:
5f1792a96efffe84b220edfa7286fa7670806226aa4d0b4b6c7e33ddd7747bf9

## Dependencies

The original engine includes FFmpeg (LGPL), libplist (LGPL), OpenSSL 3
(Apache-2.0), SDL2/SDL2_ttf (zlib), FreeType, libpng, zlib, brotli, bzip2,
Dear ImGui (MIT) and GPL playfair code. Their origins and notices remain in the
original THIRD_PARTY_NOTICES.md. Upstream vcpkg.json pins dependency versions
and recipes. Compatible dynamic libraries can be replaced in the extracted
native directory; no technical restriction prevents doing so.

AirNet/AirVideo are compiled with portable Zig 0.14.1 (LLVM/MinGW-w64).
Associated MIT/MinGW-w64/LLVM runtime notices are in airnet-licenses.
https://ziglang.org/download/0.14.1/
Iced 1.21.0 (MIT) is a build-only dependency of the PE relocation tool.
https://github.com/icedland/iced
https://www.nuget.org/packages/Iced/1.21.0
FFmpeg public frame API:
https://github.com/FFmpeg/FFmpeg/blob/n8.0/libavutil/frame.h
Microsoft scheduling and socket policy references:
https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service
https://microsoft.github.io/windows-docs-rs/doc/windows/Win32/Networking/WinSock/constant.SIO_TCP_SET_ACK_FREQUENCY.html

Microsoft Visual C++ runtime 14.44.35211 DLLs are bundled app-locally from the
official Microsoft.VC.14.44.17.14.CRT.Redist.X64.base Visual Studio distribution.
The archive URL, Microsoft's catalog SHA-256 and source catalog are pinned in
vc-runtime.json. Only release x64 CRT DLLs are extracted; no runtime installer
is run and no DLL is taken from the development PC. They remain copyright
Microsoft Corporation and are not covered by the GPL. Microsoft's original
runtime license is preserved in licenses/Microsoft-VC-Runtime-LICENSE.docx and
copied with provenance into airnet-licenses in the native payload.
https://visualstudio.microsoft.com/license-terms/vs2022-cruntime/
https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files

Apple, iPhone, AirPlay and macOS are Apple Inc. trademarks. AirMirror is an
independent community project, not an official Apple application.
