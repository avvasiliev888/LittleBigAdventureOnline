#!/usr/bin/env python3
"""Сжатие сырого инвентаря клипов в файл, по которому проверяются наборы.

Зачем отдельный шаг: сырой инвентарь снимает Unity (`Albion.Editor.ClipInventory`)
и кладёт во временную папку. Хранить в репозитории нужно не его целиком, а
только модели-доноры — те, у которых есть клипы. Так файл остаётся читаемым и
не раздувается перечислением мебели и коллизий.

Модель без клипов в инвентарь не попадает намеренно: она не может ничего дать
набору анимаций, а её присутствие только увеличивает файл.

Использование:
    python tools/unity/make-clip-inventory.py <сырой.json> [--out путь]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

DEFAULT_OUT = "docs/imports/clip-inventory.json"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("raw", help="JSON, снятый Albion.Editor.ClipInventory")
    parser.add_argument("--out", default=DEFAULT_OUT)
    args = parser.parse_args()

    if not os.path.isfile(args.raw):
        print("ОШИБКА: нет файла " + args.raw)
        return 2

    with open(args.raw, encoding="utf8") as handle:
        data = json.load(handle)

    donors = []
    for model in data.get("models", []):
        clips = model.get("clips") or []
        if not clips:
            continue

        donors.append(
            {
                "path": model["path"],
                "rig": model.get("rig", "none"),
                # Only what a set needs: the name to reference, the length to judge it by,
                # and whether it loops, which decides how it is sampled.
                "clips": [
                    {"name": c["name"], "length": c["length"], "loop": c["loop"]}
                    for c in clips
                ],
            }
        )

    donors.sort(key=lambda m: m["path"])

    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    with open(args.out, "w", encoding="utf8", newline="\n") as handle:
        json.dump({"models": donors}, handle, ensure_ascii=False, indent=1)
        handle.write("\n")

    total_clips = sum(len(m["clips"]) for m in donors)
    print(f"доноров={len(donors)} клипов={total_clips} файл={args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())