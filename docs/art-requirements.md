# Art requirements — что скачать и как положить

Цель — заменить процедурных персонажей профессиональными FBX-моделями. Код больше не
генерирует тела, одежду и текстуры. Пайплайн сам настраивает импорт, аватары, URP-материалы
и prefab-обёртки; от вас нужны только авторские ассеты.

## 0. Базовый CC0-набор уже подключён

В проект автоматически скачаны и импортированы **KayKit** (лицензия CC0 1.0, авторы —
Kay Lousberg):

```text
Assets/Art/Characters/KayKit/Knight.fbx              герой, Humanoid, 75 анимаций
Assets/Art/Characters/KayKit/Skeleton_Minion.fbx     синий моб
Assets/Art/Characters/KayKit/Skeleton_Rogue.fbx      жёлтый моб
Assets/Art/Characters/KayKit/Skeleton_Warrior.fbx    красный моб
Assets/Art/Weapons/KayKit/*.fbx                      меч, топор, посох, арбалет, кинжал
Assets/Art/Environment/KayKit/*.obj                  бочка, ящики, сундук, факел, стол, стул, колонна, флаг
Assets/Art/KayKit/LICENSE-*.txt                      тексты CC0
```

Готовые prefab'ы лежат в `Assets/Art/{Characters,Weapons,Environment}/Generated/`.
Герой и мобы уже прописаны в `Assets/Resources/Characters/WarriorVisual.asset` и
`MobVisuals.asset`. После изменения исходников выполните:

1. **Albion → Art → Prepare All Imported Art** — импорт FBX/OBJ, Avatar, URP Lit, prefab.
2. **Albion → Art → Configure KayKit All** — привязка Knight, пяти видов оружия и трёх
   скелетов к зонам.
3. **Albion → Art → Validate Imported Art** — проверка Avatar/URP Lit/CharacterVisuals.

Исходные URL и лицензии — в `docs/MANUAL_DOWNLOAD.md`. Это временный stylized low-poly
набор: для финального «Albion-level» визуала нужны Synty/Mixamo или собственные ассеты.

## 1. Куда класть

Строго в эти папки (одна папка = один пак):

```text
Assets/Art/
  Characters/
    HeroKnight/
      SK_HeroKnight.fbx
      Textures/
      Animations/        (FBX с клипами, можно с @ в имени)
      LICENSE.txt
    Bandit/
    Wolf/
  Weapons/
    Sword/
      SM_Sword.fbx
      Textures/
      LICENSE.txt
    Axe/
    Bow/
    Staff/
  Environment/
    ForestPack/
```

`Assets/Art/Characters/Generated/`, `Weapons/Generated/` и `Environment/Generated/` —
**генерируемые** prefab-обёртки, их не редактируйте вручную. После копирования файлов:

1. Дайте Unity импортировать FBX (postprocessor сработает сам).
2. Или выполните **Albion → Art → Prepare All Imported Art**.
3. Готовый prefab появится в `Assets/Art/Characters/Generated/<Имя>.prefab`.
4. Для готового героя выполните **Albion → Art → Configure KayKit Hero** (он сам
   назначит prefab, контроллер и оружие в `WarriorVisual.asset`). Для произвольного
   prefab'а можно вручную перетащить его в `WarriorVisual.asset`; контроллер с prefab'а
   имеет приоритет.

## 2. Минимальный набор для теста

| Слот | Что нужно | Пример бесплатного источника |
|---|---|---|
| Герой | 1 рыцарь/воин в T-pose, Humanoid, с текстурами | **Mixamo**: `Knight`, `Warrior`, `Paladin` (Adobe account, free) |
| Мобы | 3 разные модели: волк, бандит, голем/скелет | **Quaternius** (CC0): `Ultimate Monsters`, `RPG Characters`; **Kenney**: `Animated Characters` |
| Оружие | меч, топор, лук, посох, кинжал; пивот в рукояти | **Quaternius**: `Fantasy Weapons`; **Kenney**: `Weapon Kit` |
| Анимации | idle, walk, run, attack, hit, death; в идеале лук/посох | **Mixamo** animation library |
| Окружение | 2–3 дерева, 2 камня, руина/дом | **Quaternius**: `Ultimate Nature Pack`; **Kenney**: `Nature Kit` |
| Опционально | стилизованный пак целиком | **Synty POLYGON** (платные: `Fantasy Kingdom`, `Knights`, `Elven Kingdom`; есть free starter — проверьте текущую страницу Asset Store) |

### 2.1. Mixamo

Бесплатно после входа в Adobe-аккаунт:

- Персонажи: `Knight`, `Warrior`, `Paladin`, `Rogue`, `Mage`, `Vampire`, `Werewolf`.
- Анимации: `Idle`, `Walking`, `Running`, `Sword Slash`, `Attack`, `Hit Reaction`, `Death`.
- Скачивать: **FBX Binary**, **T-pose**, **Without Keyframes** (или with keyframes — не важно),
  30 fps, scale 1.
- Для каждой анимации — отдельный FBX. Названия клипов при импорте приведутся к
  `Idle`/`Walk`/`Run`/`Attack`/`Hit`/`Dead` автоматически, если в имени есть
  `idle/walk/run/attack|slash/hit|pain/death`.

### 2.2. Synty POLYGON

Если бюджет позволяет — самый близкий к Albion стиль:

- `POLYGON - Fantasy Kingdom Pack`
- `POLYGON - Knights Pack`
- `POLYGON - Elven Kingdom Pack`
- `POLYGON - Dungeon Pack` (для окружения)

Проверьте лицензию Synty: обычно разрешено использовать в играх, но **нельзя**
перепродавать сами ассеты. Для бесплатного теста берите free starter, если он доступен
на Asset Store, иначе **Quaternius** (CC0) — он без лицензионных ограничений.

### 2.3. Quaternius / Kenney

- **Quaternius**: `Ultimate Modular Men`, `Ultimate Monsters`, `RPG Characters`,
  `Fantasy Weapons`, `Ultimate Nature Pack`. Формат FBX или glTF; берите FBX.
- **Kenney**: `Animated Characters 1`, `Animated Characters 2`, `Weapon Kit`,
  `Nature Kit`, `Fantasy Town Kit`. CC0, но стиль более «игрушечный», чем Albion.

## 3. Технические требования к FBX

### Персонаж

- Humanoid rig, **T-pose**, один скелет, root на уровне стоп.
- Масштаб: 1 Unity unit = 1 метр; рост 1.8–2.0 м. Если пак в сантиметрах — импорт
  выставит `globalScale`, но лучше сразу правильный.
- Forward — +Z (Unity); Mixamo обычно +Z после конвертации. Если модель смотрит назад,
  поправьте `Yaw` в `WarriorVisual.asset` или в prefab.
- До 50k треугольников на героя, до 15k на моба; 1–4 SkinnedMeshRenderer.
- Текстуры: PNG/TGA, 2K (можно 1K для мобов), **внешние файлы**, не embedded.
- Карты:
  - `*_Albedo.png` / `*_BaseColor.png` — sRGB;
  - `*_Normal.png` — Normal Map, linear;
  - `*_MetallicSmoothness.png` или `*_MaskMap.png` — linear (R metallic, A smoothness);
  - `*_AO.png` / `*_Occlusion.png` — linear;
  - `*_Emission.png` — sRGB, если есть.
- Имена материалов лучше `M_<Character>_<Part>`; пайплайн создаст `M_...` в
  `.../Materials`.
- Collider/Rigidbody в prefab не нужны; если есть — пайплайн отключит.

### Оружие

- Отдельный FBX, **пивот ровно в точке хвата** (grip), +Y — вдоль клинка/древка.
- Масштаб в метрах; меч ~1.0–1.2 м, кинжал ~0.35 м, лук ~1.4 м, посох ~1.8 м.
- Без скелета и анимаций. Материалы — URP Lit/PBR.
- В `WarriorVisual.asset` назначьте prefab'ы оружия в массив **Weapons**
  (`Kind` = Sword/Axe/Bow/Staff/Dagger); для KayKit это делает
  **Albion → Art → Configure KayKit Hero**. Если пивот/ориентация отличаются, задайте
  `Local Position/Euler/Scale` у соответствующего элемента. В рантайме
  `CharacterVisualSettings` передаёт этот массив в `CharacterVisuals`.

### Анимации

- Humanoid, in-place (без root motion), 30 fps.
- Отдельные FBX на клип: `Idle`, `Walk`, `Run`, `Attack`, `Hit`, `Dead`.
- Для лука/посоха можно отдельные `AttackBow`, но `CharacterVisuals` сейчас вызывает
  общий `Attack`; для точного соответствия оружию добавьте состояния в контроллер и
  переходы по параметру `WeaponKind` (заготовка есть в компоненте).

## 3.1. Шейдер персонажей

Персонажи и оружие используют **Universal Render Pipeline/Lit**. Это стандартный PBR-шейдер
Unity: он корректно принимает Albedo (`_BaseMap`), Normal (`_BumpMap`), Metallic/Smoothness
(`_MetallicGlossMap`), Occlusion (`_OcclusionMap`) и Emission (`_EmissionMap`). Пайплайн
включает нужные keywords (`_NORMALMAP`, `_METALLICSPECGLOSSMAP`, `_OCCLUSIONMAP`, `_EMISSION`),
поэтому текстуры не «схлопываются» в один цвет.

Старый `Albion/Toon Lit` остаётся для окружения/эффектов и **не применяется** к импортированным
персонажам. Вызов `FantasyVisuals.ToonWarrior`, который раньше перекрашивал warrior в плоские
цвета, удалён из пути персонажа.

## 4. Что делает автоматический пайплайн

`Assets/Editor/ArtImportPipeline.cs`:

- `OnPreprocessModel`: для `Assets/Art/Characters` ставит Humanoid + Avatar
  `CreateFromThisModel`, включает анимации/блендшейпы, правильные нормали/тангенты,
  отключает камеры/свет, ставит внешние материалы.
- `OnPostprocessModel`: планирует сборку prefab; создаёт
  `Assets/Art/<Category>/Generated/<Name>.prefab`.
- `ArtMaterialUtility`: конвертирует материалы в **Universal Render Pipeline/Lit**,
  переносит `_BaseMap`, `_BumpMap`, `_MetallicGlossMap`/`_MaskMap`, `_OcclusionMap`,
  `_EmissionMap` и включает соответствующие keywords. Нормали/маски не теряются.
- `ArtTexturePostprocessor`: нормалям ставит `NormalMap`, mask/metallic/AO — linear,
  albedo — sRGB.
- `CharacterVisuals` добавляется на prefab, находит сокеты `RightHandSocket` /
  `WeaponSocket.R` / `mixamorig:RightHand` (или humanoid bone) и умеет монтировать
  prefab оружия.
- `Albion → Art → Validate Imported Art` проверяет: есть ли Avatar/Animator,
  `CharacterVisuals`, все ли материалы — URP Lit.

### Автоконтроллер анимаций

Пайплайн собирает контроллер **только из клипов самого FBX** (не из соседних персонажей
в той же папке): `Assets/Art/Characters/Generated/<Name>Controller.controller` со
состояниями `Idle/Walk/Run/Attack/Hit/Dead` и параметрами `Speed/Dead/Attack/Hit`.
Для каждого слота выбирается представительный клип (`Idle`, `Walking_A`, `Running_A`,
`1H_Melee_Attack_Chop`, `Hit_A`, `Death_A`), а не случайный вариант. Если клипов ещё
нет, импортируйте анимации и выполните **Albion → Art → Create Locomotion Controller
From Selected Model** (выделите FBX персонажа).

## 5. Если модель не заработала

| Симптом | Причина / решение |
|---|---|
| Розовые материалы | Материал не URP Lit; запустите `Prepare All Imported Art`, проверьте `Validate Imported Art` |
| Персонаж в T-pose и не двигается | Нет RuntimeAnimatorController; создайте контроллер или назначьте свой на prefab |
| Оружие в центре тела | Не найден сокет; назовите кость `RightHandSocket` или назначьте `RightHandSocket` в `CharacterVisuals` вручную |
| Оружие смотрит не туда | Поправьте `Local Position/Euler/Scale` в `CharacterVisuals.Weapons` |
| Модель провалилась под землю / огромная | Неверный масштаб FBX; пайплайн нормирует по `Height` в `WarriorVisual.asset`, но проверьте T-pose |
| Анимация «скользит» | Клип с root motion; отключите root motion в импорте или берите in-place версию |
| Нет текстур | Текстуры embedded/потеряны; положите PNG рядом с FBX и переимпортируйте |

## 6. Лицензии

Для каждого пака положите `LICENSE.txt` в его папку. Mixamo разрешает использование
персонажей/анимаций в проектах, но проверьте Adobe terms; Synty — коммерческая лицензия,
не перепродавать ассеты; Quaternius/Kenney — CC0 (указание авторства не обязательно, но
приятно). Не смешивайте лицензии в одном паке без записи.
