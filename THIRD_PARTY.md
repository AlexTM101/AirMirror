# Сторонние компоненты

Код оболочки и адаптеров AirMirror распространяется под GPL-3.0. Сторонние компоненты сохраняют собственные лицензии и уведомления.

| Компонент | Использование | Источник и уведомления |
| --- | --- | --- |
| AirPlay-Windows v0.4.1 / UxPlay | Приём и декодирование AirPlay | [Исходники](native/upstream), [изменения AirMirror](native/THIRD_PARTY.md), [уведомления оригинала](native/upstream/THIRD_PARTY_NOTICES.md) |
| .NET 8 | Среда выполнения C# | [Лицензия](src/AirMirror.WinUI/Notices/DotNet-LICENSE.txt), [сторонние компоненты](src/AirMirror.WinUI/Notices/DotNet-ThirdPartyNotices.txt) |
| Windows App SDK 1.8.260921001 / WinUI 3 | Интерфейс и работа с окнами | [Windows App SDK](https://github.com/microsoft/WindowsAppSDK), [WinUI](https://github.com/microsoft/microsoft-ui-xaml), [уведомления Base](src/AirMirror.WinUI/Notices/WindowsAppSDK-Base-NOTICE.txt), [уведомления WinUI](src/AirMirror.WinUI/Notices/WinUI-NOTICE.txt) |
| Zig 0.14.1 / LLVM / MinGW-w64 | Сборка нативных адаптеров | [Zig](https://ziglang.org/download/0.14.1/); уведомления среды выполнения входят в архив приёмника |
| Iced 1.21.0 | Инструмент проверки и переноса машинных инструкций при сборке | [Iced](https://github.com/icedland/iced), MIT |

Встроенный нативный архив включает FFmpeg, libplist, OpenSSL, SDL2, FreeType и другие зависимости оригинального приёмника. Подробности, лицензии и происхождение сохранены в `native/upstream/THIRD_PARTY_NOTICES.md` и внутри архива.

Microsoft Visual C++ runtime распространяется рядом с приёмником на условиях Microsoft; он не перелицензирован под GPL. Архив приёмника и среда WinUI распаковываются при запуске. Динамические библиотеки приёмника доступны в каталоге кэша и не защищены от замены совместимыми версиями.

AirMirror не является продуктом Apple и не использует их фирменные значки в качестве значка приложения.
