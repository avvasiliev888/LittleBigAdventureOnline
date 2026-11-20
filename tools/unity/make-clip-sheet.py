#!/usr/bin/env python3
"""Сборка кадров клипов в GIF и контактный лист.

Зачем: решение «годится ли пакет как донор анимаций» принимается глазами, а
не по именам файлов. Один GIF на клип отвечает на вопрос «как это выглядит»
быстрее любого списка, а контактный лист показывает весь каталог разом.

Вход — папки `frames` и `stills` от `Albion.Editor.ClipContactSheet`.
Выход — `clips/<имя>.gif` и `sheet.png`.

Использование:
    python tools/unity/make-clip-sheet.py <папка out> [--fps 12]
"""

from __future__ import annotations

import argparse
import glob
import os
import sys
from collections import defaultdict

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("ERROR: нужен Pillow (pip install pillow)")
    sys.exit(2)


def build_gifs(out_dir: str, fps: int) -> int:
    """Один GIF на клип: кадры сгруппированы по префиксу до последнего '_'."""
    frames_dir = os.path.join(out_dir, "frames")
    clips_dir = os.path.join(out_dir, "clips")
    os.makedirs(clips_dir, exist_ok=True)

    groups: dict[str, list[str]] = defaultdict(list)
    for path in sorted(glob.glob(os.path.join(frames_dir, "*.png"))):
        name = os.path.splitext(os.path.basename(path))[0]
        label = name.rsplit("_", 1)[0]
        groups[label].append(path)

    written = 0
    duration = max(20, int(1000 / max(1, fps)))
    for label, paths in sorted(groups.items()):
        images = [Image.open(p).convert("RGB") for p in paths]
        if not images:
            continue
        # Пауза на последнем кадре, чтобы глаз успел увидеть конечную позу.
        frames = images + [images[-1]] * max(2, fps // 4)
        target = os.path.join(clips_dir, label + ".gif")
        frames[0].save(
            target,
            save_all=True,
            append_images=frames[1:],
            duration=duration,
            loop=0,
            optimize=True,
        )
        written += 1

    return written


def build_filmstrips(out_dir: str, thumb: int = 120) -> int:
    """Кадры клипа в один ряд — движение видно на статичной картинке.

    GIF в чате не всегда проигрывается: превью показывает только первый кадр.
    Развёртка надёжнее — она читается как раскадровка, и по ней видно, машет
    ли персонаж рукой, бьёт сверху или приседает.
    """
    frames_dir = os.path.join(out_dir, "frames")
    strips_dir = os.path.join(out_dir, "strips")
    os.makedirs(strips_dir, exist_ok=True)

    groups: dict[str, list[str]] = defaultdict(list)
    for path in sorted(glob.glob(os.path.join(frames_dir, "*.png"))):
        name = os.path.splitext(os.path.basename(path))[0]
        groups[name.rsplit("_", 1)[0]].append(path)

    written = 0
    pad = 4
    for label, paths in sorted(groups.items()):
        images = [Image.open(p).convert("RGB") for p in paths]
        if not images:
            continue
        cell_h = int(thumb * 1.25)
        for image in images:
            image.thumbnail((thumb, cell_h), Image.LANCZOS)
        width = sum(i.width for i in images) + pad * (len(images) + 1)
        strip = Image.new("RGB", (width, cell_h + pad * 2), (26, 28, 33))
        x = pad
        for image in images:
            strip.paste(image, (x, pad))
            x += image.width + pad
        strip.save(os.path.join(strips_dir, label + ".png"))
        written += 1

    return written


def build_sheet(out_dir: str, columns: int = 6, thumb: int = 150) -> str | None:
    """Контактный лист: все клипы каталога, подпись под каждым кадром."""
    stills_dir = os.path.join(out_dir, "stills")
    paths = sorted(glob.glob(os.path.join(stills_dir, "*.png")))
    if not paths:
        return None

    rows = (len(paths) + columns - 1) // columns
    pad = 6
    label_height = 14
    cell_w = thumb + pad * 2
    cell_h = int(thumb * 1.25) + label_height + pad * 2

    sheet = Image.new("RGB", (columns * cell_w, rows * cell_h), (26, 28, 33))
    draw = ImageDraw.Draw(sheet)

    for index, path in enumerate(paths):
        image = Image.open(path).convert("RGB")
        image.thumbnail((thumb, int(thumb * 1.25)), Image.LANCZOS)
        column = index % columns
        row = index // columns
        x = column * cell_w + pad
        y = row * cell_h + pad
        sheet.paste(image, (x, y))
        name = os.path.splitext(os.path.basename(path))[0]
        draw.text((x, y + image.height + 2), name, fill=(210, 214, 220))

    target = os.path.join(out_dir, "sheet.png")
    sheet.save(target)
    return target


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("out", help="папка out от ClipContactSheet")
    parser.add_argument("--fps", type=int, default=12)
    args = parser.parse_args()

    if not os.path.isdir(args.out):
        print("ERROR: нет папки: " + args.out)
        return 2

    gifs = build_gifs(args.out, args.fps)
    strips = build_filmstrips(args.out)
    sheet = build_sheet(args.out)

    print(f"gifs={gifs}")
    print(f"strips={strips}")
    print(f"sheet={sheet}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
