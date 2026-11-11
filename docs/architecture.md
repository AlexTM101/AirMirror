# AirMirror 1.5.2 — WinUI 3

GPL-3.0 Windows 11 x64 AirPlay receiver. The interface and settings use
Microsoft.UI.Xaml / WinUI 3, with no WPF or Windows Forms reference in the new
application. Engine.cs preserves the existing receiver and its preferences.
Native video remains GPU-backed in a child HWND; no screen capture, web view,
pixel readback or software transcoding was added by the interface migration.

## Build on Windows

From the extracted source-package root, in PowerShell:

```powershell
.\scripts\Get-Tools.ps1
.\build.ps1
```

The compiler is portable .NET SDK 8.0.428, verified against Microsoft's SHA-512.
NuGet restores pinned Windows App SDK 1.8.260921001 and Windows SDK BuildTools
10.0.26100.9457 to work/nuget. No Visual Studio or system-wide SDK install
is required. Build tools require Internet only when first downloading packages.
For development, build.ps1 -BuildOnly creates an ordinary output folder.

The normal publish output is dist/AirMirror-1.5.2-WinUI3.exe, a compressed
self-contained executable, including .NET and the Windows App SDK. It works
without a separately installed .NET or Windows App SDK runtime. Components are
extracted on launch; this is a single distribution file, not zero-extraction.
The Windows App SDK's SingleFile target generates the required resources.pri
and registration-free WinRT manifest. App.xaml supplies compiled XAML metadata
and standard WinUI controls resources; the rest of the UI is built in C#.
ProjectPriFileName is explicitly resources.pri so the published executable can
be renamed without breaking resource lookup. Test the actual renamed, isolated
single-file publish output, not only the development build directory.

The previously verified native payload is embedded from native/engine.zip;
native/engine.sha256 identifies it. Its original source and modifications are
included in native/upstream and native. See those notices and the root LICENSE.
To rebuild the native payload from the pinned original release, first follow
docs/BUILDING.md; native/build.ps1 builds only the payload in this repository.

## Behavior retained

* Native Windows caption; main view has no IP/FPS counters or lower-left status.
* iPhone screen and audio reception through the same AirPlay engine.
* 30/60 FPS, GPU decode option, optional VSync, Smooth/Fast modes and bounded queue.
* Automatic portrait/landscape fitting with aspect preservation.
* Full monitor fullscreen, F11/Esc, floating controls and idle cursor hiding.
* Minimize to tray, double-click/menu restore, explicit exit, and activation of
  the same instance by launching its executable again.
* Existing receiver name, display preferences, identity and recovery behavior.

Native icons and menus use Win32 Shell APIs. Main controls, settings, dialogs
and the floating fullscreen controls use WinUI 3. No startup task is installed.
Full shutdown removes the tray icon and closes the native child using its
existing job-object and graceful-stop mechanisms.

## Verification

AirMirror.WinUI.exe --self-test checks actual startup and mDNS registration,
native child attachment, real native transport/decoded-frame queue tests,
actual WinUI portrait/landscape resizing and matching child bounds, tray
restoration from normal/maximized states, and physical fullscreen restoration.
This includes moving the normal window before maximizing, leaving fullscreen
back to its saved bounds, and checking the actual floating controls window.
The same RTSP SETUP/TEARDOWN smoke fixture passes synthetic frames through the
real renderer. It is enabled only in the self-test child.

--self-test-recovery additionally kills its own native child, waits for bounded
recovery and checks that identity is preserved. AIRMIRROR_DATA_DIR redirects
cache/logs for testing; native preferences retain the existing dedicated
roaming AirMirror-Local namespace. Do not run either test during a real session.
tests/Run-Integration.ps1 preserves the roaming preference files around a run.

tests/Run-Engine.ps1 checks the real backend without WinUI: same-size reconnect,
concurrent stop/dispose, verified bundle extraction and damaged cache repair
without changing identity. It forces fresh builds when switching its intentional
bad-hash fixture. Native regression probes additionally exercise a real 200 ms
Winsock timeout, resumed bytes and EOF while tracking the MMCSS lifetime.

Interactive verification also observed a real iPhone portrait stream and
fullscreen display. Sustained frame pacing and audio quality are not measured
by the synthetic checks. This migration does not promise improved frame rates.
