#!/usr/bin/env python3
"""Карта ассетов Unity: зависимости по GUID и что отслеживается git.

Зачем
-----
Разбор ассетов Unity — самая дорогая ручная работа в проекте. Чтобы понять,
что попадёт в коммит, приходится глазами просматривать сотни файлов и
сопоставлять GUID из контроллеров с `.meta`-файлами. Это уже делалось один
раз и стоило очень много токенов; скрипт заменяет это вычислением.

    python tools/unity/asset-map.py                 # сводка
    python tools/unity/asset-map.py --deps WarriorVisual
    python tools/unity/asset-map.py --untracked     # что не в git и почему
    python tools/unity/asset-map.py --lfs           # проверка LFS-указателей

Что считается
-------------
1. Замыкание зависимостей ассета по `guid:` — какие файлы реально нужны
   для его работы и все ли они в git.
2. Файлы в Assets, не отслеживаемые git (кандидаты «забыли добавить»).
3. Бинарники, которые обязаны лежать в LFS, но лежат напрямую.

Код возврата 1 — найдены забытые зависимости или бинарник вне LFS.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(ROOT, "unity-client", "Assets")

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")

# Ассеты, ради которых стоит проверять замыкание. Ключ — имя, по которому
# спрашивают, значение — путь относительно unity-client/.
CORE_ASSETS = {
    "WarriorVisual": "Assets/Resources/Characters/WarriorVisual.asset",
    "WarriorLocomotion": "Assets/Resources/Characters/WarriorLocomotion.controller",
    "Warrior": "Assets/dRealWarriorFantasyCharacter/Prefabs/Warrior.prefab",
}

# Каталоги, которые не являются частью игры: маркетинговые материалы чужого
# ассет-пака и неиспользуемые модели. Держать их вне git — осознанное решение.
IGNORED_PREFIXES = (
    "Assets/AllStarCharacterLibrary/Models/Materials/Marketing/",
    "Assets/AllStarCharacterLibrary/Models/Characters/",
    "Assets/AllStarCharacterLibrary/Models/RootAnimsFemale/",
    "Assets/AllStarCharacterLibrary/Models/StatusBars/",
    "Assets/AllStarCharacterLibrary/Models/CollisionHelpers/",
    "Assets/AllStarCharacterLibrary/Models/Materials/",
    "Assets/_Recovery/",
)

EXT_LFS = (".fbx", ".png", ".tga", ".jpg", ".jpeg", ".psd", ".wav", ".mp3",
           ".ogg", ".obj", ".blend", ".zip", ".unitypackage", ".ttf")


def git(*args: str) -> str:
    out = subprocess.run(["git", *args], cwd=ROOT, capture_output=True,
                         text=True, encoding="utf-8", errors="replace")
    return out.stdout


def build_guid_index() -> dict[str, str]:
    """guid -> путь относительно корня репозитория."""
    index: dict[str, str] = {}
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if d not in ("Library", "Temp", "obj")]
        for name in filenames:
            if not name.endswith(".meta"):
                continue
            full = os.path.join(dirpath, name)
            try:
                with open(full, encoding="utf-8", errors="replace") as fh:
                    head = fh.read(400)
            except OSError:
                continue
            m = GUID_RE.search(head)
            if m:
                rel = os.path.relpath(full, ROOT).replace(os.sep, "/")
                index[m.group(1)] = rel
    return index


def tracked_set() -> set[str]:
    return {p for p in git("ls-files").splitlines() if p}


def deps_of(asset_rel: str) -> list[str]:
    """GUID, на которые ссылается ассет (файл или .meta)."""
    full = os.path.join(ROOT, asset_rel)
    if not os.path.exists(full):
        # имя без пути — искать в CORE_ASSETS
        cand = CORE_ASSETS.get(asset_rel)
        if cand:
            full = os.path.join(ROOT, "unity-client", cand)
            asset_rel = "unity-client/" + cand
    try:
        with open(full, encoding="utf-8", errors="replace") as fh:
            text = fh.read()
    except OSError:
        return []
    return sorted(set(GUID_RE.findall(text)))


def cmd_deps(name: str) -> int:
    key = name.replace(".asset", "").replace(".controller", "").replace(".prefab", "")
    rel = CORE_ASSETS.get(key)
    if not rel:
        print(f"Не знаю ассет «{name}». Известные: {', '.join(CORE_ASSETS)}")
        return 2
    asset_rel = "unity-client/" + rel
    index = build_guid_index()
    tracked = tracked_set()
    guids = deps_of(asset_rel)

    print(f"Зависимости {key} ({asset_rel})")
    print(f"  ссылок: {len(guids)}")
    missing = []
    for g in guids:
        target = index.get(g)
        if not target:
            print(f"  ✗ БИТАЯ ССЫЛКА  {g} — .meta с таким guid не найден")
            missing.append(g)
            continue
        ok = target in tracked
        mark = "·" if ok else "✗"
        note = "" if ok else "  НЕ В GIT"
        print(f"  {mark} {target}{note}")
        if not ok:
            missing.append(target)
    if missing:
        print(f"\nПроблем: {len(missing)} — клон не соберётся без них.")
        return 1
    print("\nВсе зависимости отслеживаются git.")
    return 0


def cmd_untracked() -> int:
    tracked = tracked_set()
    forgotten, ignored = [], []
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if d not in ("Library", "Temp", "obj")]
        for name in filenames:
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, ROOT).replace(os.sep, "/")
            if rel in tracked:
                continue
            ua = rel.split("unity-client/", 1)[-1]
            (ignored if ua.startswith(IGNORED_PREFIXES) else forgotten).append(rel)

    print(f"НЕ отслеживается git: {len(forgotten) + len(ignored)}")
    print(f"  из них намеренно (чужой ассет-пак, маркетинг): {len(ignored)}")
    print(f"  требуют решения: {len(forgotten)}")
    for rel in sorted(forgotten)[:40]:
        print(f"  ? {rel}")
    if len(forgotten) > 40:
        print(f"  … ещё {len(forgotten) - 40}")
    return 1 if forgotten else 0


def cmd_lfs() -> int:
    """Бинарник обязан быть LFS-указателем. Ловим файлы, залитые напрямую."""
    bad = []
    for rel in sorted(tracked_set()):
        if not rel.lower().endswith(EXT_LFS):
            continue
        blob = git("cat-file", "-s", f"HEAD:{rel}").strip()
        if not blob.isdigit():
            continue
        size = int(blob)
        # LFS-указатель весит ~130 байт и начинается с version https://git-lfs
        if size < 200:
            continue
        head = git("show", f"HEAD:{rel}")[:40]
        if "git-lfs" in head:
            continue
        bad.append((rel, size))
    if not bad:
        print("Все бинарники в LFS.")
        return 0
    print(f"Бинарники вне LFS: {len(bad)}")
    for rel, size in bad:
        print(f"  ✗ {rel}  {size // 1024} КБ в истории напрямую")
    return 1


def cmd_summary() -> int:
    index = build_guid_index()
    tracked = tracked_set()
    print(f"Ассетов (.meta): {len(index)}  |  файлов под git: {len(tracked)}")
    print()
    worst = 0
    for key in CORE_ASSETS:
        asset_rel = "unity-client/" + CORE_ASSETS[key]
        guids = deps_of(asset_rel)
        absent = [g for g in guids if index.get(g) not in tracked]
        status = "OK" if not absent else f"{len(absent)} НЕ В GIT"
        print(f"  {key:<20} ссылок={len(guids):<3} {status}")
        worst += len(absent)
    print()
    print("Подробности: --deps <имя>, --untracked, --lfs")
    return 1 if worst else 0


def main() -> None:
    ap = argparse.ArgumentParser(description="Карта ассетов Unity и их зависимостей.")
    ap.add_argument("--deps", metavar="АССЕТ",
                    help="замыкание зависимостей: " + ", ".join(CORE_ASSETS))
    ap.add_argument("--untracked", action="store_true",
                    help="файлы в Assets, не отслеживаемые git")
    ap.add_argument("--lfs", action="store_true",
                    help="проверить, что бинарники лежат в LFS")
    opts = ap.parse_args()

    if opts.deps:
        sys.exit(cmd_deps(opts.deps))
    if opts.untracked:
        sys.exit(cmd_untracked())
    if opts.lfs:
        sys.exit(cmd_lfs())
    sys.exit(cmd_summary())


if __name__ == "__main__":
    main()
