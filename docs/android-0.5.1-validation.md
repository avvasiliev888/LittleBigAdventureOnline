# Android 0.5.1 — исправление запуска (2026-11-11)

## Причина

В APK `Application.streamingAssetsPath` имеет вид `jar:file:///…/base.apk!/assets`.
Раньше ClientRuntime передавал этот URI в ClientContent.LoadFromDirectory, который использует
File.Exists / File.ReadAllText. Загрузка actors.json завершалась ошибкой ещё до создания
UnityView, панели подключения и сенсорного управления. Видна была только статическая арена.

Теперь все девять JSON загружаются асинхронно через UnityWebRequest и передаются в общий
ClientContent.LoadFromJson. Валидация и SHA-256 остаются общими с сервером; пропущенные конфиги
не заменяются дефолтными. Ошибка старта показывается отдельной центральной панелью.

## Артефакт

- APK: `unity-client/Builds/android/AlbionPrototype.apk` (генерируемый, не в git).
- Package: `com.albion.prototype`; versionName: `0.5.1`; versionCode: `2`.
- ARM64 / IL2CPP; min SDK 26, target SDK 36; INTERNET permission.
- Размер: 36 882 865 байт.
- SHA-256: `102CB055A887F46B4B7C142527E3E70797D4D5D8169A9BB8A45C5157C5FE571A`.
- APK Signature Scheme v2: проверена, Android Debug certificate.
- LAN-IP по умолчанию в ресурсе сборки: `192.168.1.174`; порт: `9050`.

## Выполненные проверки

1. `dotnet test Prototype.sln -c Debug --nologo`: **425/425 PASS**.
   Release-прогон прервался на копировании DLL из-за работающих серверов; их не останавливали.
2. `dotnet run --project src/Server -c Debug --no-build -- --validate-config-only`: **PASS**.
   Хеш: `c355316d910c8af253fd6a93cb45e0ae2134684e3c660b7fb99989264533b57f`.
3. `pwsh -File tools/unity/agent-smoke.ps1 -KeepProject -SkipPlugins`: **PASS**.
   Unity Editor PlayMode: snapshots=5, deltas=4, visuals=8, mobs=6, corpses=0.
4. Новый регрессионный набор в том же Unity smoke: **PASS**:
   - UnityWebRequest читает `file://` URI с пробелами в пути;
   - UTF-8 BOM не меняет хеш относительно серверного чтения;
   - все девять секций дают тот же конфиг-хеш;
   - невалидный JSON не создаёт ClientContent;
   - отсутствующий JSON завершает загрузку с именем файла в ошибке.
5. Android batch build: Unity exit 0, APK собран.
6. aapt2: версия/архитектура/SDK/INTERNET соответствуют данным выше.
7. apksigner verify: **PASS**.
8. Все девять `assets/Config/*.json` из готового APK побайтно совпадают с `config/*.json` сервера.
9. Simulation.Core не содержит UnityEngine.
10. Scoped `dotnet format whitespace --folder` для затронутых Unity C# файлов и diff check: **PASS**.
    Общий `dotnet format --verify-no-changes` сообщает старые CRLF/порядок импортов в неизменённых
    .NET-файлах; массовое форматирование в этот bugfix не включено.

## Граница проверки

Физический Android-телефон не подключён к ADB. Запуск нового APK на Android и фактическое
чтение `jar:file://` на устройстве **не проверены**. Editor smoke проверяет реализацию
UnityWebRequest на локальных URI, а не Android runtime. Статическая проверка APK не заменяет
проверку на телефоне.

Проверка на телефоне: установить 0.5.1 поверх 0.5.0, открыть приложение, дождаться панели IP,
нажать «Подключиться» при работающем UDP-сервере в той же LAN; проверить игрока, HUD, стик и удар.
После длительного сворачивания/смены Wi-Fi может потребоваться перезапуск приложения:
восстановление UDP-сессии — отдельный известный недостаток, в это исправление не входит.
