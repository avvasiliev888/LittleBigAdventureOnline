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
DEFAULT_INVENTORY = "docs/imports/clip-inventory-kaykit.json"

# Набор обязан покрывать эти роли: без них моб не может ни стоять, ни идти,
# ни драться, ни умирать. Отсутствие роли — ошибка, а не предупреждение.
REQUIRED_ROLES = ("idle", "walk", "run", "attack", "death", "hit")


def load_clips(path: str) -> set[str]:
    with open(path, encoding="utf8") as handle:
        data = json.load(handle)

    clips: set[str] = set()
    for model in data.get("models", []):
        for clip in model.get("clips", []):
            clips.add(clip["name"])
    return clips


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sets", default=DEFAULT_SETS)
    parser.add_argument("--inventory", default=DEFAULT_INVENTORY)
    args = parser.parse_args()

    for path in (args.sets, args.inventory):
        if not os.path.isfile(path):
            print("ОШИБКА: нет файла " + path)
            return 2

    clips = load_clips(args.inventory)
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
                if name not in clips:
                    problems.append(f"{label}: роль '{role}' -> нет клипа '{name}'")

    if problems:
        print("НАБОРЫ НЕ ГОДНЫ:")
        for problem in problems:
            print("  - " + problem)
        return 1

    print(f"OK  наборов: {len(data.get('sets', []))}, все клипы найдены в инвентаре.")
    return 0


if __name__ == "__main__":
    sys.exit(main())