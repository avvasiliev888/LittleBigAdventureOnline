# ClientRuntime

**Файл:** `unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs`  
**Размер:** 629 строк ≈ 6 707 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Точка сборки клиента: ввод, конвейер, HUD, камера.

**Из кода:** Точка входа Unity-клиента (M6, M7): создаёт мир, транспорт, конвейер клиента и крутит кадры. С M7 клиент предсказывает движение игрока и реконсилирует его по ServerAck; в HUD видно Скрипт запускается сам после загрузки сцены, поэтому отдельная сцена/префаб не нужны. Настройки берутся из переменных окружения ALBION_SERVER_HOST / ALBION_SERVER_PORT или из аргументов командной строки -serverHost / -serverPort.

## API (детерминированно из графа trail)

```
- L18-L628  class ClientRuntime  public sealed class ClientRuntime : MonoBehaviour
- L48-L48  variable Pipeline  public ClientPipeline? Pipeline => _pipeline;
- L51-L51  variable View  public UnityView? View => _view;
- L54-L54  variable Input  public UnityInputSource? Input => _input;
- L57-L67  method AutoStart  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
- L69-L90  method Awake  private void Awake()
- L92-L128  method LoadContentAndStart  private IEnumerator LoadContentAndStart(string host, int port)
- L130-L141  method ReadMobileDefaultHost  private static string ReadMobileDefaultHost()
- L143-L208  method StartSession  private void StartSession(string host, int port)
- L210-L235  method ResolveAbilityTarget  private uint ResolveAbilityTarget()
- L237-L246  method PlayerPosition  private Vector3 PlayerPosition()
- L253-L256  method TargetPosition  private Vector3? TargetPosition(uint entityId)
- L263-L266  method AbilityRange  private float AbilityRange()
- L272-L278  method IsPlayerStopped  private bool IsPlayerStopped()
- L285-L289  method ApproachSlack  private float ApproachSlack()
- L301-L306  method TargetStatus  private AbilityTargetStatus TargetStatus(uint entityId)
- L308-L312  method OnGroundClick  private void OnGroundClick(Vector3 point)
- L314-L345  method Update  private void Update()
- L347-L354  method OnDestroy  private void OnDestroy()
- L356-L386  method OnGUI  private void OnGUI()
- L388-L398  method DrawBuildVersion  private static void DrawBuildVersion()
- L400-L413  method DrawStartupPanel  private void DrawStartupPanel()
- L415-L464  method DrawConnectPanel  private void DrawConnectPanel()
- L466-L494  method ConnectFromPanel  private void ConnectFromPanel()
- L496-L513  method EnsureCamera  private void EnsureCamera()
- L515-L525  method EnsureLight  private static void EnsureLight()
- L527-L561  method EnsureGround  private static void EnsureGround()
- L563-L566  method EnsureAmbient  private static void EnsureAmbient()
- L568-L587  method EnsureVolume  private static Volume EnsureVolume()
- L589-L607  method UpdateFps  private void UpdateFps()
- L609-L627  method ReadSetting  private static string ReadSetting(string environmentName, string argumentName, string fallback)
```

## Как пользоваться

1. Нужен ответ про ClientRuntime — читайте здесь, а не unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ClientRuntime`.
