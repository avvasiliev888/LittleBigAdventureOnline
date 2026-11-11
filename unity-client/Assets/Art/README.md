# Art

Здесь лежат **только внешние** авторские модели. Никакие текстуры/модели кодом не
генерируются.

```text
Assets/Art/
  Characters/<PackName>/   FBX + Textures + Materials персонажей
  Weapons/<PackName>/      FBX оружия (пивот в рукояти)
  Environment/<PackName>/  деревья, камни, здания, props
  Characters/Generated/    автоматически собранные prefab-обёртки
  Weapons/Generated/       автоматически собранные prefab-обёртки оружия
  Environment/Generated/   автоматически собранные prefab-обёртки окружения
```

Уже подключён CC0-набор **KayKit**: см. `docs/MANUAL_DOWNLOAD.md` и
`docs/art-requirements.md`. Один пак — одна папка с `LICENSE.txt`. После копирования
FBX/OBJ запустите **Albion → Art → Prepare All Imported Art** (или просто дайте Unity
импортировать файл: postprocessor сделает то же самое), затем
**Albion → Art → Configure KayKit All** и **Validate Imported Art**.

Не переименовывайте `Generated/` вручную: скрипт пересоберёт prefab при повторном импорте
FBX/OBJ. Если хотите ручную настройку — создайте prefab в `Characters/` рядом с паком,
добавьте `CharacterVisuals` и укажите его в `WarriorVisual.asset`.
