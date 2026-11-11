# MANUAL_DOWNLOAD — ассеты, которые не удалось скачать автоматически

## Статус: базовый набор скачан автоматически

Ручное скачивание для базового набора **не требуется**. В проекте уже лежат CC0-ассеты
KayKit, скачанные с официальных GitHub-репозиториев `KayKit-Game-Assets`:

| Что | Файлы | Куда положено |
|---|---|---|
| Герой (Knight, Humanoid, 75 анимаций) | `Knight.fbx`, `knight_texture.png`, `rogue_texture.png`, `mage_texture.png`, `barbarian_texture.png` | `Assets/Art/Characters/KayKit/` |
| Мобы (3 скелета, 90+ анимаций) | `Skeleton_Minion.fbx`, `Skeleton_Rogue.fbx`, `Skeleton_Warrior.fbx`, `skeleton_texture.png` | `Assets/Art/Characters/KayKit/` |
| Оружие (5 видов) | `sword_1handed.fbx`, `axe_1handed.fbx`, `staff.fbx`, `crossbow_1handed.fbx`, `dagger.fbx` | `Assets/Art/Weapons/KayKit/` |
| Окружение (8 props) | `*.obj`, `*.mtl`, `dungeon_texture.png` | `Assets/Art/Environment/KayKit/` |
| Лицензии CC0 | `LICENSE-Adventurers.txt`, `LICENSE-Skeletons.txt`, `LICENSE-Dungeon.txt` | `Assets/Art/KayKit/` |

Повторное скачивание:

```powershell
pwsh -File tools/unity/download-kaykit.ps1
# принудительно перезаписать:
pwsh -File tools/unity/download-kaykit.ps1 -Force
```

## 1. Прямые ссылки (CC0)

### KayKit Adventurers 1.0

- Репозиторий: <https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0>
- ZIP: <https://codeload.github.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0/zip/refs/heads/main>
- Лицензия: <https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Character-Pack-Adventures-1.0/main/LICENSE.txt> (CC0 1.0)
- Персонаж: `addons/kaykit_character_pack_adventures/Characters/fbx/Knight.fbx`
- Оружие: `addons/kaykit_character_pack_adventures/Assets/fbx/*.fbx`

### KayKit Skeletons 1.0

- Репозиторий: <https://github.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0>
- ZIP: <https://codeload.github.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0/zip/refs/heads/main>
- Лицензия: <https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Character-Pack-Skeletons-1.0/main/LICENSE.txt> (CC0 1.0)
- Мобы: `addons/kaykit_character_pack_skeletons/Characters/fbx/Skeleton_*.fbx`

### KayKit Dungeon Remastered 1.0

- Репозиторий: <https://github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0>
- ZIP: <https://codeload.github.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0/zip/refs/heads/main>
- Лицензия: <https://raw.githubusercontent.com/KayKit-Game-Assets/KayKit-Dungeon-Remastered-1.0/main/LICENSE.txt> (CC0 1.0)
- В ZIP лежат `Assets/obj/*.obj` и `Assets/texture/dungeon_texture.png`; FBX-версии
  намеренно исключены из GitHub-архива (`.gitattributes export-ignore`), но Unity
  импортирует OBJ нативно.

## 2. Если GitHub недоступен — скачать вручную

Откройте страницы itch.io (бесплатные пакеты, может потребоваться вход/капча):

1. <https://kaylousberg.itch.io/kaykit-adventurers> — «Adventurers» (free).
2. <https://kaylousberg.itch.io/kaykit-skeletons> — «Skeletons» (free).
3. <https://kaylousberg.itch.io/kaykit-dungeon-remastered> — «Dungeon Remastered» (free).

Скачайте ZIP и распакуйте **только нужные FBX/OBJ**:

```text
Assets/Art/Characters/KayKit/    Knight.fbx, Skeleton_Minion.fbx,
                                 Skeleton_Rogue.fbx, Skeleton_Warrior.fbx,
                                 knight/rogue/mage/barbarian_texture.png,
                                 skeleton_texture.png
Assets/Art/Weapons/KayKit/       sword_1handed.fbx, axe_1handed.fbx, staff.fbx,
                                 crossbow_1handed.fbx, dagger.fbx
Assets/Art/Environment/KayKit/   *.obj, *.mtl, dungeon_texture.png
Assets/Art/KayKit/               LICENSE-*.txt
```

FBX должны лежать **рядом с PNG-текстурой**: пайплайн ищет внешние текстуры
`RecursiveUp` и иначе оставит материал белым.

## 3. Что нажать в Unity после копирования

1. Дождитесь окончания импорта (Progress в правом нижнем углу).
2. **Albion → Art → Prepare All Imported Art** — Avatar Humanoid, URP Lit, prefab'ы.
3. **Albion → Art → Configure KayKit All** — Knight в `WarriorVisual.asset`, 5 оружий,
   3 скелета в `MobVisuals.asset` по зонам blue/yellow/red.
4. **Albion → Art → Validate Imported Art** — в Console должно быть
   `[art] Validation passed`.
5. Запустите smoke/игру и проверьте, что герой не в T-pose, а мобы — скелеты.

## 4. Анимации

В KayKit уже есть готовые клипы:

- Adventurers: **75 анимаций**, включая `Idle`, `Walking_A`, `Running_A`,
  `1H_Melee_Attack_Chop`, `Hit_A`, `Death_A`.
- Skeletons: **90+ анимаций** с теми же базовыми слотами.

Поэтому **Mixamo для базового набора не нужен**. Контроллер
`KnightController.controller` собирается из клипов самого Knight.fbx; для мобов —
из их собственных FBX.

Если позже понадобится больше анимаций (лук/посох/двуручное оружие):

1. Mixamo → выберите персонажа или загрузите свой FBX.
2. Animation → скачайте **FBX Binary**, **Without Skin**, 30 fps, in-place.
3. Назовите файл с `@`, например `Knight@AttackBow.fbx`, и положите в
   `Assets/Art/Characters/KayKit/Animations/`.
4. Нажмите **Albion → Art → Create Locomotion Controller From Selected Model**.
5. Проверьте `CharacterVisuals` на prefab: состояния `Attack`, `Hit`, `Dead`.

Mixamo требует Adobe-аккаунт; автоматически скачать без авторизации нельзя, и мы
намеренно не обходим эту защиту.

## 5. Лицензии и ограничения

- KayKit — **CC0 1.0**, коммерческое использование разрешено, указание автора
  необязательно. Тексты лицензий сохранены в `Assets/Art/KayKit/`.
- Это stylized low-poly в стиле KayKit, **не Albion-level**. Для финального визуала
  нужны Synty POLYGON / Mixamo / собственные модели.
- Не добавляйте ассеты Unity Asset Store без проверки лицензии; не перепродавайте
  исходные паки.
