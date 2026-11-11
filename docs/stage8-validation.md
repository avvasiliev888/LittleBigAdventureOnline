# Stage 8 — visual rework (rejected procedural art)

## Решение

Stage 8 с процедурными гуманоидами из сфер/кубов **отклонён**. Код генерации удалён:
`ProceduralHumanoidVisual`, `ProceduralWeaponBuilder`, `ProceduralCharacterGenerator`,
`ProceduralCharacterChecks`, `Stage8VisualChecks`. Релиз 0.7.0 с этой графикой отозван;
вместо него собран 0.7.1 с импортированным warrior. Визуал 0.7.1 — временный fallback,
но процедурных гуманоидов в нём нет.

## Что оставлено как временный fallback

- Игрок: импортированный `dRealWarriorFantasyCharacter/Prefabs/Warrior.prefab` с реальным
  Humanoid-скелетом, Animator и URP Lit-материалами (Albedo + Normal + Metallic).
- Мобы: старый `FantasyVisuals.CreateMob` (волк/голем), пока не поставлены профессиональные
  модели. Это заглушка, а не целевой арт.
- `FantasyVisuals.ToonWarrior` больше не вызывается: он перекрашивал текстурного warrior в
  плоские цвета и добавлял outline-оболочки. Материалы остаются URP Lit.

## Новый пайплайн

- `Assets/Editor/ArtImportPipeline.cs` — импорт FBX из `Assets/Art/`:
  Humanoid + Avatar, анимации, нормали/тангенты, URP Lit-материалы, prefab-обёртки,
  автоконтроллер из клипов.
- `Assets/Editor/ArtMaterialUtility.cs` — перенос `_BaseMap`, `_BumpMap`,
  `_MetallicGlossMap`/`_MaskMap`, `_OcclusionMap`, `_EmissionMap` и keywords URP Lit.
- `Assets/Scripts/View/CharacterVisuals.cs` — универсальный адаптер prefab'а: Animator,
  сокеты рук, монтаж authored-оружия, состояния Idle/Walk/Run/Attack/Hit/Dead.
- `docs/art-requirements.md` — список бесплатных ассетов, форматы и куда класть.

## Проверка

- Компиляция Unity: успешно.
- Импортированный warrior regression: prefab, URP Lit/Albedo, скелет, анимации,
  оружие, смерть/респавн, удалённая интерполяция — PASS.
- Runtime smoke: `PASS snapshots=5 deltas=4 visuals=8 mobs=6 corpses=0`.
- Скриншот: `artifacts/art-pipeline-smoke.png` (временный fallback, не финальный арт).

## Релиз 0.7.1

Собран APK/Desktop/Server **0.7.1** с импортированным warrior и старыми мобами как
временным fallback. Это откат от 0.7.0, а не финальный арт.

Следующий шаг — получить от заказчика реальные Synty/Mixamo/Quaternius FBX по
`docs/art-requirements.md`, прогнать `Albion → Art → Prepare All Imported Art` и только
после визуальной приёмки выпускать следующий релиз.
