# ClientRuntime

**Файл:** `unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs`  
**Размер:** 551 строк ≈ 5 731 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Точка сборки клиента: ввод, конвейер, HUD, камера.

**Из кода:** Точка входа Unity-клиента (M6, M7): создаёт мир, транспорт, конвейер клиента и крутит кадры. С M7 клиент предсказывает движение игрока и реконсилирует его по ServerAck; в HUD видно Скрипт запускается сам после загрузки сцены, поэтому отдельная сцена/префаб не нужны. Настройки берутся из переменных окружения ALBION_SERVER_HOST / ALBION_SERVER_PORT или из аргументов командной строки -serverHost / -serverPort.

## API (детерминированно из графа trail)

```
- L18-L550  class ClientRuntime  public sealed class ClientRuntime : MonoBehaviour
- L48-L48  variable Pipeline  public ClientPipeline? Pipeline => _pipeline;
- L51-L51  variable View  public UnityView? View => _view;
- L54-L54  variable Input  public UnityInputSource? Input => _input;
- L57-L67  method AutoStart  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
- L69-L90  method Awake  private void Awake()
- L92-L128  method LoadContentAndStart  private IEnumerator LoadContentAndStart(string host, int port)
- L130-L141  method ReadMobileDefaultHost  private static string ReadMobileDefaultHost()
- L143-L203  method StartSession  private void StartSession(string host, int port)
- L205-L217  method ResolveAbilityTarget  private uint ResolveAbilityTarget()
- L219-L228  method PlayerPosition  private Vector3 PlayerPosition()
- L230-L234  method OnGroundClick  private void OnGroundClick(Vector3 point)
- L236-L267  method Update  private void Update()
- L269-L276  method OnDestroy  private void OnDestroy()
- L278-L308  method OnGUI  private void OnGUI()
- L310-L320  method DrawBuildVersion  private static void DrawBuildVersion()
- L322-L335  method DrawStartupPanel  private void DrawStartupPanel()
- L337-L386  method DrawConnectPanel  private void DrawConnectPanel()
- L388-L416  method ConnectFromPanel  private void ConnectFromPanel()
- L418-L435  method EnsureCamera  private void EnsureCamera()
- L437-L447  method EnsureLight  private static void EnsureLight()
- L449-L483  method EnsureGround  private static void EnsureGround()
- L485-L488  method EnsureAmbient  private static void EnsureAmbient()
- L490-L509  method EnsureVolume  private static Volume EnsureVolume()
- L511-L529  method UpdateFps  private void UpdateFps()
- L531-L549  method ReadSetting  private static string ReadSetting(string environmentName, string argumentName, string fallback)
```

## Как пользоваться

1. Нужен ответ про ClientRuntime — читайте здесь, а не unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ClientRuntime`.
