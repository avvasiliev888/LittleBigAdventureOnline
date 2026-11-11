# Art

Здесь лежат **только внешние** авторские модели. Никакие текстуры/модели кодом не
генерируются.

```text
Assets/Art/
  Characters/<PackName>/   FBX + Textures + Materials персонажей
  Weapons/<PackName>/      FBX оружия (пивот в рукояти)
  Environment/<PackName>/  деревья, камни, здания
  Characters/Generated/    автоматически собранные prefab-обёртки
  Weapons/Generated/       автоматически собранные prefab-обёртки оружия
```

Один пак — одна папка с `LICENSE.txt`. После копирования FBX запустите
**Albion → Art → Prepare All Imported Art** (или просто дайте Unity импортировать файл:
postprocessor сделает то же самое). Инструкция и список бесплатных ассетов:
`docs/art-requirements.md`.

Не переименовывайте `Generated/` вручную: скрипт пересоберёт prefab при повторном импорте
FBX. Если хотите ручную настройку — создайте prefab в `Characters/` рядом с паком,
добавьте `CharacterVisuals` и укажите его в `WarriorVisual.asset`.
