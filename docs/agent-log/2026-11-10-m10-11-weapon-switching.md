# M10.11 — переключение оружия (меч/топор)

## Симптом

У персонажа одновременно в руках меч и топор. Причина — vendor-префаб `Warrior.prefab`: под
`RightHandSocket` добавлены **оба** nested prefab instance:

- `Warrior_Sword` (guid `0961b69d…`);
- `Warrior_Axe` (guid `de21a125…`).

Оба активны; аниматор один, поэтому оба меша следуют за правой рукой.

## Решение

- Новый `WeaponVisuals` (presentation-only) находит `Warrior_Sword` и `Warrior_Axe` среди потомков
  (включая inactive), при `Attach` вызывает `Equip(Sword)`: меч `SetActive(true)`, топор
  `SetActive(false)`.
- `CharacterVisualSettings.Create` добавляет компонент сразу после `Instantiate`, до показа
  персонажа, поэтому топор не мелькает в первом кадре.
- `UnityInputSource.PollFrame`: `1`/`Keypad1` → `WeaponKind.Sword`, `2`/`Keypad2` → `WeaponKind.Axe`;
  callback `_view.EquipWeapon` переключает только локального игрока.
- `UnityView.EquipWeapon` берёт `WeaponVisuals` из `EntityVisual`; удалённые игроки и манекен остаются
  с мечом.
- HUD-подсказка: `1/2 — оружие`.

**Протокол не менялся.** `AbilityCommand` по-прежнему не содержит типа оружия; урон/радиус/кулдаун
общие. Это сознательно: M10.11 — визуальный срез, серверная модель оружия — кандидат на M11+.

## Анимация

Контроллер имеет один humanoid-клип `1HAttack`; меч и топор — дочерние объекты hand socket, поэтому
активное оружие автоматически следует за рукой. Отдельных axe/sword клипов в проекте нет; выдумывать
их не стал. `ValidateWeaponAnimation`:

1. проверяет дефолт (меч active, топор inactive);
2. для Sword и Axe: `Equip`, проверка ровно одного активного объекта;
3. запоминает позицию `EquippedTransform`, вызывает `PlayAttack`, проверяет state `Attack` и что
   позиция оружия изменилась;
4. возвращает `Idle` и восстанавливает меч.

## Проверки

- `ConfigureAndVerify` (`m10-11-configure.log`): exit 0, GPU 13/13,
  `Warrior weapon checks passed: sword by default, 1/2 toggles one mesh, equipped weapon follows Attack`,
  combat checks passed.
- Живой прогон desktop-билда с сервером:
  ```
  WeaponVisuals: equipped Sword (sword active=True, axe active=False)
  WeaponVisuals: equipped Axe (sword active=False, axe active=True)
  WeaponVisuals: equipped Sword (sword active=True, axe active=False)
  ```
  Клавиши 2/1 доходят до игры; лог доказывает ровно один активный меш.
- Desktop/mobile: Succeeded, size **93 170 070**, errors0; `Assembly-CSharp.dll` 18:34:15 / 18:37:47.
- .NET-тесты: **223/223**, coverage 100% (код симуляции не менялся).
- CI `bf2fd8d`: [pull_request 42846775323](https://github.com/avvasiliev888/albion-prototype/actions/runs/42846775323)
  и [push 42846786129](https://github.com/avvasiliev888/albion-prototype/actions/runs/42846786129) — success.

## Остаточный риск

- Выбор оружия не передаётся на сервер и не реплицируется другим клиентам. Для M10.11 это ок;
  серверная модель оружия потребует поля в `AbilityCommand`/снапшоте и отдельного решения по урону.
- Меч и топор используют один клип; если появятся отдельные анимации, `WeaponVisuals` можно
  расширить выбором клипа без изменения префаба.
- Клавиши работают, когда окно игры в фокусе; в HUD добавлена подсказка.
