# Проверки AirMirror

Команды выполняются из корня репозитория на Windows 11 x64. Используйте .NET SDK из `global.json`; при переносимой установке передайте путь к `dotnet.exe` через `-DotNetPath`.

## Без запуска интерфейса

```powershell
.\tests\Run-Engine.ps1 -DotNetPath (Get-Command dotnet).Source
.\scripts\Test-BuildScripts.ps1
```

Первый скрипт компилирует настоящий `Engine.cs` в отдельном стенде. Проверяются переподключение с прежним разрешением, общая остановка и конкурентное освобождение ресурсов, контрольная сумма встроенного архива, восстановление отсутствующих и изменённых DLL, сохранение идентификатора и повтор после неудачной распаковки. Проверка заведомо неверной суммы и обычная проверка пересобираются начисто: переключение ресурсов нельзя доверять инкрементальной сборке. Последняя сборка всегда содержит правильную сумму.

Второй скрипт проверяет загрузчик на повреждённом кэше, прерывании, неверной сумме, резервном адресе и полном/неполном наборе Visual C++. Подменяется только граница HTTP; временные файлы и распаковка настоящие. Пользовательские настройки не используются.

## Нативная сеть

После получения Zig командой `native/Get-Zig.ps1`:

```powershell
New-Item -ItemType Directory -Force work/tests | Out-Null
$zig = '.\work\tools\zig-x86_64-windows-0.14.1\zig.exe'
& $zig cc -target x86_64-windows-gnu -std=c11 -O2 -Wall -Wextra -Werror tests/native/airnet-timeout.c -lws2_32 -lavrt -o work/tests/airnet-timeout.exe
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
.\work\tests\airnet-timeout.exe
if ($LASTEXITCODE -ne 0) { throw 'Timeout regression failed' }
& $zig cc -target x86_64-windows-gnu -std=c11 -O2 -Wall -Wextra -Werror tests/native/airnet-loopback.c -lws2_32 -lavrt -o work/tests/airnet-loopback.exe
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
.\work\tests\airnet-loopback.exe
if ($LASTEXITCODE -ne 0) { throw 'Loopback regression failed' }
```

Оба стенда включают настоящий `native/airnet.c`. Первый задаёт возвращаемые ошибки на границах Winsock/MMCSS; второй использует настоящий локальный TCP, тайм-аут 200 мс, продолжение передачи и EOF. Если служба MMCSS недоступна, второй явно сообщает об этом; поведение её времени жизни проверяет первый стенд.

## Настоящее окно WinUI

На интерактивном рабочем столе, после закрытия работающей AirMirror:

```powershell
.\tests\Run-Integration.ps1 -Executable .\dist\AirMirror-1.5.2-WinUI3.exe -DataDirectory .\work\self-test
.\tests\Run-Integration.ps1 -Executable .\dist\AirMirror-1.5.2-WinUI3.exe -DataDirectory .\work\recovery-test -Recovery
```

Проверяются настоящий дочерний приёмник, обнаружение, локальный RTSP, видеовывод синтетических кадров, очередь кадров, размеры при повороте, трей, полный экран, сохранение положения и плавающая панель. Второй запуск намеренно завершает только свой дочерний процесс и проверяет восстановление с прежним идентификатором. Скрипт сохраняет и восстанавливает реальные файлы параметров в roaming-профиле; отдельный профиль Windows предпочтителен. Отчёт `self-test.txt` обязан начинаться с `PASS`, код завершения — 0.

Автоматические проверки не измеряют длительный FPS и звук с iPhone по Wi-Fi. Также они не заменяют проверку разных GPU, мониторов с разным DPI и закрытия настроек во время настоящего запроса Windows о настройке доступа к сети.
