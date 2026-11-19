# ClientRuntime

**Файл:** `unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs`  
**Размер:** 585 строк ≈ 6 137 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Точка сборки клиента: ввод, конвейер, HUD, камера.

**Из кода:** Точка входа Unity-клиента (M6, M7): создаёт мир, транспорт, конвейер клиента и крутит кадры. С M7 клиент предсказывает движение игрока и реконсилирует его по ServerAck; в HUD видно Скрипт запускается сам после загрузки сцены, поэтому отдельная сцена/префаб не нужны. Настройки берутся из переменных окружения ALBION_SERVER_HOST / ALBION_SERVER_PORT или из аргументов командной строки -serverHost / -serverPort.

## API (детерминированно из графа trail)

```
- L18-L584  class ClientRuntime  public sealed class ClientRuntime : MonoBehaviour
- L48-L48  variable Pipeline  public ClientPipeline? Pipeline => _pipeline;
- L51-L51  variable View  public UnityView? View => _view;
- L54-L54  variable Input  public UnityInputSource? Input => _input;
- L57-L67  method AutoStart  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
- L69-L90  method Awake  private void Awake()
- L92-L128  method LoadContentAndStart  private IEnumerator LoadContentAndStart(string host, int port)
- L130-L141  method ReadMobileDefaultHost  private static string ReadMobileDefaultHost()
- L143-L206  method StartSession  private void StartSession(string host, int port)
- L208-L220  method ResolveAbilityTarget  private uint ResolveAbilityTarget()
- L222-L231  method PlayerPosition  private Vector3 PlayerPosition()
- L238-L241  method TargetPosition  private Vector3? TargetPosition(uint entityId)
- L248-L251  method AbilityRange  private float AbilityRange()
- L258-L262  method ApproachSlack  private float ApproachSlack()
- L264-L268  method OnGroundClick  private void OnGroundClick(Vector3 point)
- L270-L301  method Update  private void Update()
- L303-L310  method OnDestroy  private void OnDestroy()
- L312-L342  method OnGUI  private void OnGUI()
- L344-L354  method DrawBuildVersion  private static void DrawBuildVersion()
- L356-L369  method DrawStartupPanel  private void DrawStartupPanel()
- L371-L420  method DrawConnectPanel  private void DrawConnectPanel()
- L422-L450  method ConnectFromPanel  private void ConnectFromPanel()
- L452-L469  method EnsureCamera  private void EnsureCamera()
- L471-L481  method EnsureLight  private static void EnsureLight()
- L483-L517  method EnsureGround  private static void EnsureGround()
- L519-L522  method EnsureAmbient  private static void EnsureAmbient()
- L524-L543  method EnsureVolume  private static Volume EnsureVolume()
- L545-L563  method UpdateFps  private void UpdateFps()
- L565-L583  method ReadSetting  private static string ReadSetting(string environmentName, string argumentName, string fallback)
```

## Как пользоваться

1. Нужен ответ про ClientRuntime — читайте здесь, а не unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ClientRuntime`.
