# ClientRuntime

**Файл:** `unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs`  
**Размер:** 662 строк ≈ 7 116 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Точка сборки клиента: ввод, конвейер, HUD, камера.

**Из кода:** Точка входа Unity-клиента (M6, M7): создаёт мир, транспорт, конвейер клиента и крутит кадры. С M7 клиент предсказывает движение игрока и реконсилирует его по ServerAck; в HUD видно Скрипт запускается сам после загрузки сцены, поэтому отдельная сцена/префаб не нужны. Настройки берутся из переменных окружения ALBION_SERVER_HOST / ALBION_SERVER_PORT или из аргументов командной строки -serverHost / -serverPort.

## API (детерминированно из графа trail)

```
- L18-L661  class ClientRuntime  public sealed class ClientRuntime : MonoBehaviour
- L48-L48  variable Pipeline  public ClientPipeline? Pipeline => _pipeline;
- L51-L51  variable View  public UnityView? View => _view;
- L54-L54  variable Input  public UnityInputSource? Input => _input;
- L57-L67  method AutoStart  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
- L69-L90  method Awake  private void Awake()
- L92-L128  method LoadContentAndStart  private IEnumerator LoadContentAndStart(string host, int port)
- L130-L141  method ReadMobileDefaultHost  private static string ReadMobileDefaultHost()
- L143-L208  method StartSession  private void StartSession(string host, int port)
- L210-L236  method ResolveAbilityTarget  private uint ResolveAbilityTarget()
- L243-L262  method CameraFrustum  private ViewFrustum? CameraFrustum()
- L264-L273  method PlayerPosition  private Vector3 PlayerPosition()
- L280-L283  method TargetPosition  private Vector3? TargetPosition(uint entityId)
- L290-L293  method AbilityRange  private float AbilityRange()
- L299-L305  method IsPlayerStopped  private bool IsPlayerStopped()
- L312-L316  method ApproachSlack  private float ApproachSlack()
- L328-L333  method TargetStatus  private AbilityTargetStatus TargetStatus(uint entityId)
- L335-L339  method OnGroundClick  private void OnGroundClick(Vector3 point)
- L341-L378  method Update  private void Update()
- L380-L387  method OnDestroy  private void OnDestroy()
- L389-L419  method OnGUI  private void OnGUI()
- L421-L431  method DrawBuildVersion  private static void DrawBuildVersion()
- L433-L446  method DrawStartupPanel  private void DrawStartupPanel()
- L448-L497  method DrawConnectPanel  private void DrawConnectPanel()
- L499-L527  method ConnectFromPanel  private void ConnectFromPanel()
- L529-L546  method EnsureCamera  private void EnsureCamera()
- L548-L558  method EnsureLight  private static void EnsureLight()
- L560-L594  method EnsureGround  private static void EnsureGround()
- L596-L599  method EnsureAmbient  private static void EnsureAmbient()
- L601-L620  method EnsureVolume  private static Volume EnsureVolume()
- L622-L640  method UpdateFps  private void UpdateFps()
- L642-L660  method ReadSetting  private static string ReadSetting(string environmentName, string argumentName, string fallback)
```

## Как пользоваться

1. Нужен ответ про ClientRuntime — читайте здесь, а не unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ClientRuntime`.
