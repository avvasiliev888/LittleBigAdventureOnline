#!/usr/bin/env python3
"""Распаковка .unitypackage в проект БЕЗ Unity.

Зачем: `AssetDatabase.ImportPackage` в batchmode асинхронен — метод,
вызванный сразу после него, видит пустой проект. Пакет `.unitypackage` —
это обычный tar.gz, внутри на каждый ассет папка GUID с файлами
`pathname` (куда класть), `asset` (сам файл), `asset.meta` (его .meta).
Поэтому распаковка делается детерминированно и за секунды, а Unity потом
открывает проект, где всё уже на месте.

Использование:
    python tools/unity/extract-package.py "<путь к .unitypackage>" <папка проекта>
    python tools/unity/extract-package.py --list "<путь к .unitypackage>"
"""

from __future__ import annotations

import argparse
import os
import sys
import tarfile

# Каталоги, которые нельзя перезаписывать: чужой пакет не должен затирать
# настройки проекта или наши скрипты.
PROTECTED = (
    "Assets/Editor/",
    "Assets/Scripts/",
    "Assets/Plugins/",
    "ProjectSettings/",
    "Packages/",
)


def read_pathname(handle: tarfile.TarFile, member: tarfile.TarInfo) -> str:
    """Путь внутри Assets. Unity пишет его с завершающим "\\n00"."""
    raw = handle.extractfile(member).read().decode("utf8", "ignore")
    return raw.split("\n")[0].strip().rstrip("\x00").strip()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("package", help="путь к .unitypackage")
    parser.add_argument("project", nargs="?", help="папка проекта Unity")
    parser.add_argument("--list", action="store_true", help="только показать содержимое")
    args = parser.parse_args()

    if not os.path.isfile(args.package):
        print("ERROR: пакет не найден: " + args.package)
        return 2

    if not args.list and not args.project:
        print("ERROR: нужна папка проекта (или --list)")
        return 2

    written = 0
    skipped = 0

    with tarfile.open(args.package, "r:gz") as handle:
        members = {m.name: m for m in handle.getmembers()}
        guids = sorted({m.name.split("/")[0] for m in members.values() if "/" in m.name})

        for guid in guids:
            pathname = members.get(guid + "/pathname")
            if pathname is None:
                continue

            target = read_pathname(handle, pathname)
            if not target:
                continue

            if args.list:
                print(target)
                continue

            # Папки пакета перечислены отдельными записями без файла: создать их
            # всё равно нужно, иначе первый файл в подкаталоге упадёт.
            asset = members.get(guid + "/asset")
            destination = os.path.join(args.project, target.replace("/", os.sep))

            if any(target.startswith(p) for p in PROTECTED):
                print("SKIP защищённый путь: " + target)
                skipped += 1
                continue

            if asset is None:
                os.makedirs(destination, exist_ok=True)
                continue

            os.makedirs(os.path.dirname(destination), exist_ok=True)
            with handle.extractfile(asset) as source, open(destination, "wb") as sink:
                sink.write(source.read())
            written += 1

            meta = members.get(guid + "/asset.meta")
            if meta is not None:
                with handle.extractfile(meta) as source:
                    data = source.read()
                with open(destination + ".meta", "wb") as sink:
                    sink.write(data)

    if args.list:
        return 0

    print(f"written={written} skipped={skipped}")

    # Проверка, без которой ошибка всплывёт позже и дороже: пакет, который
    # распаковался в пустоту, означает неверный путь или битый архив.
    if written == 0:
        print("ERROR: ни одного файла не записано")
        return 3

    return 0


if __name__ == "__main__":
    sys.exit(main())
