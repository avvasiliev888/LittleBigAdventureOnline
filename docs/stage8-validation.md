# Stage 8 — visual rework (rejected procedural art)

## Решение

Stage 8 с процедурными гуманоидами из сфер/кубов **отклонён**. Код генерации удалён:
`ProceduralHumanoidVisual`, `ProceduralWeaponBuilder`, `ProceduralCharacterGenerator`,
`ProceduralCharacterChecks`, `Stage8VisualChecks`. Релиз 0.7.0 с этой графикой отозван;
вместо него собран 0.7.1 с импортированным warrior. Визуал 0.7.1 — временный fallback,
но процедурных гуманоидов в нём нет.

## Что подключено после отката

- Игрок: **KayKit Knight** (CC0) — импортированный Humanoid-скелет, Animator,
  URP Lit-материалы (Albedo + Normal + Metallic), 75 анимаций.
- Мобы: **KayKit Skeletons** (CC0) — `Skeleton_Minion` (blue), `Skeleton_Rogue` (yellow),
  `Skeleton_Warrior` (red), 90+ анимаций. Старый `FantasyVisuals.CreateMob` остаётся
  fallback'ом, если `MobVisuals.asset` пуст.
- Оружие: 5 authored FBX KayKit, крепится к правой руке через
  `CharacterVisualSettings.Weapons`.
- Окружение: 8 OBJ-пропов KayKit Dungeon (бочка, ящики, сундук, факел, стол, стул,
  колонна, флаг).
- `FantasyVisuals.ToonWarrior` больше не вызывается: он перекрашивал текстурного warrior в
  плоские цвета и добавлял outline-оболочки. Материалы остаются URP Lit.

## Новый пайплайн

- `Assets/Editor/ArtImportPipeline.cs` — импорт FBX/OBJ из `Assets/Art/`:
  Humanoid + Avatar, анимации, нормали/тангенты, URP Lit-материалы, распакованные
  prefab-обёртки, автоконтроллер **только из клипов своего FBX** с представительными
  состояниями Idle/Walk/Run/Attack/Hit/Dead.
- `Assets/Editor/KayKitSetup.cs` — привязка Knight, пяти оружий и трёх скелетов к
  `WarriorVisual.asset` / `MobVisuals.asset`.
- `tools/unity/download-kaykit.ps1` — автоматическое скачивание CC0-набора с GitHub.
- `Assets/Editor/ArtMaterialUtility.cs` — перенос `_BaseMap`, `_BumpMap`,
  `_MetallicGlossMap`/`_MaskMap`, `_OcclusionMap`, `_EmissionMap` и keywords URP Lit.
- `Assets/Scripts/View/CharacterVisuals.cs` — универсальный адаптер prefab'а: Animator,
  сокеты рук, монтаж authored-оружия, состояния Idle/Walk/Run/Attack/Hit/Dead.
- `docs/art-requirements.md` — список бесплатных ассетов, форматы и куда класть.

## Проверка

- Компиляция Unity: успешно.
- Импортированный warrior regression: prefab, URP Lit/Albedo, скелет, анимации,
  оружие, смерть/респавн, удалённая интерполяция — PASS.
- KayKit import: 4 characters, 5 weapons, 8 environment prefabs; `ArtImportPipeline.Validate`
  — **Validation passed** (Avatar, CharacterVisuals, URP Lit).
- Weapon mount: все 5 authored-оружий крепятся к правой руке; меч/топор/арбалет/посох/
  кинжал отмасштабированы под рост 1.95 м.
- Runtime smoke с KayKit: `PASS snapshots=5 deltas=4 visuals=8 mobs=6 corpses=0`.
- Скриншот: `artifacts/kaykit-assets-final.png` (временный stylized low-poly, не финальный арт).

## Релиз 0.7.1

Собран APK/Desktop/Server **0.7.1** до подключения KayKit: в нём ещё старый
импортированный warrior и примитивные мобы. Это откат от 0.7.0, а не финальный арт.
Рабочее дерево уже содержит KayKit-набор; для его включения нужен следующий релиз.

Следующий шаг — визуальная приёмка KayKit, затем при необходимости заменить его на
Synty/Mixamo/собственные модели по `docs/art-requirements.md` и выпустить новый релиз.
