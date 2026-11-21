#!/usr/bin/env python3
"""Проверка наборов анимаций против реального инвентаря клипов.

Зачем: опечатка в имени клипа не даёт ошибки нигде. Unity молча оставит
состояние без анимации, моб будет стоять в T-позе, и виноватым окажется
«кривая модель» — ровно та ловушка, из-за которой в этом проекте уже были
потеряны часы (см. docs/warrior-animation-ledger.md, fileID 7400000).

Проверка идёт по инвентарю, снятому из Unity (`docs/imports/clip-inventory-kaykit.json`),
а не по догадкам о именах.

Использование:
    python tools/unity/check-animation-sets.py
    python tools/unity/check-animation-sets.py --sets config/animation-sets.json
"""

from __future__ import annotations

import argparse
import json
import os
import sys

DEFAULT_SETS = "config/animation-sets.json"
DEFAULT_INVENTORY = "docs/imports/clip-inventory.json"

# Набор обязан покрывать эти роли: без них моб не может ни стоять, ни идти,
# ни драться, ни умирать. Отсутствие роли — ошибка, а не предупреждение.
REQUIRED_ROLES = ("idle", "walk", "run", "attack", "death", "hit")


def load_inventory(path: str) -> dict[str, set[str]]:
    """Карта «папка донора -> имена клипов в ней»."""
    with open(path, encoding="utf8") as handle:
        data = json.load(handle)

    by_source: dict[str, set[str]] = {}
    for model in data.get("models", []):
        model_path = model["path"]
        folder = model_path.rsplit("/", 1)[0]
        names = by_source.setdefault(folder, set())
        for clip in model.get("clips", []):
            names.add(clip["name"])
    return by_source


def all_clips(by_source: dict[str, set[str]]) -> set[str]:
    result: set[str] = set()
    for names in by_source.values():
        result |= names
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sets", default=DEFAULT_SETS)
    parser.add_argument("--inventory", default=DEFAULT_INVENTORY)
    args = parser.parse_args()

    for path in (args.sets, args.inventory):
        if not os.path.isfile(path):
            print("ОШИБКА: нет файла " + path)
            return 2

    by_source = load_inventory(args.inventory)
    everything = all_clips(by_source)

    with open(args.sets, encoding="utf8") as handle:
        data = json.load(handle)

    problems: list[str] = []
    seen_keys: set[str] = set()

    for index, entry in enumerate(data.get("sets", [])):
        key = entry.get("key", "")
        label = f"набор[{index}] '{key}'"

        if not key:
            problems.append(f"{label}: пустой ключ")
            continue

        if key in seen_keys:
            problems.append(f"{label}: ключ повторяется, второй набор недостижим")
        seen_keys.add(key)

        source = entry.get("source", "")
        if not source:
            problems.append(f"{label}: нет поля 'source' — откуда брать клипы")
            continue

        # Клип обязан быть именно в этой папке. Иначе набор мечника тихо возьмёт
        # удар посохом из другого пакета, и заметить это можно только в игре.
        source_clips = by_source.get(source, set())

        for role in REQUIRED_ROLES:
            if role not in entry:
                problems.append(f"{label}: нет роли '{role}'")
                continue

            value = entry[role]
            names = value if isinstance(value, list) else [value]

            if not names:
                problems.append(f"{label}: роль '{role}' пуста")
                continue

            for name in names:
                if name not in everything:
                    problems.append(f"{label}: роль '{role}' -> клипа '{name}' нет нигде")
                elif name not in source_clips:
                    problems.append(
                        f"{label}: роль '{role}' -> клип '{name}' есть, но не в '{source}'"
                    )

    if problems:
        print("НАБОРЫ НЕ ГОДНЫ:")
        for problem in problems:
            print("  - " + problem)
        return 1

    print(f"OK  наборов: {len(data.get('sets', []))}, все клипы лежат в своих источниках.")
    return 0


if __name__ == "__main__":
    sys.exit(main())