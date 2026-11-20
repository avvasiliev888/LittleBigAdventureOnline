#!/usr/bin/env python3
"""Галерея клипов: один HTML, открывается двойным кликом.

Зачем: заказчик принимает решение глазами. GIF в чате показывает только
первый кадр, а развёртки по одной неудобно листать. Одна страница с
движущимися клипами и каталогом решает вопрос за один просмотр.

Использование:
    python tools/unity/make-clip-gallery.py <папка out> [--title "..."]
"""

from __future__ import annotations

import argparse
import glob
import os
import sys

TEMPLATE = """<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<title>{title}</title>
<style>
  body {{ background:#15171c; color:#dfe3e8; font:14px/1.5 "Segoe UI",Arial,sans-serif;
         margin:0; padding:28px 32px 60px; }}
  h1 {{ font-size:20px; font-weight:600; margin:0 0 4px; }}
  .sub {{ color:#8b93a0; margin-bottom:26px; }}
  h2 {{ font-size:15px; font-weight:600; margin:32px 0 12px; color:#aab3c0;
        text-transform:uppercase; letter-spacing:.06em; }}
  .clips {{ display:flex; flex-wrap:wrap; gap:18px; }}
  .clip {{ background:#1d2027; border:1px solid #2a2f39; border-radius:8px;
           padding:12px; }}
  .clip img {{ display:block; border-radius:4px; image-rendering:auto; }}
  .clip .name {{ margin-top:9px; font-weight:600; }}
  .clip .meta {{ color:#7f8794; font-size:12px; }}
  .strip {{ margin:8px 0 0; max-width:100%; overflow-x:auto; }}
  .strip img {{ display:block; max-width:100%; height:auto; }}
  table {{ border-collapse:collapse; margin-top:8px; }}
  th,td {{ text-align:left; padding:5px 16px 5px 0; border-bottom:1px solid #23272f; }}
  th {{ color:#8b93a0; font-weight:600; }}
</style>
</head>
<body>
<h1>{title}</h1>
<div class="sub">{subtitle}</div>
{body}
</body>
</html>
"""


def build_gallery(out_dir: str, title: str) -> str:
    clips_dir = os.path.join(out_dir, "clips")
    strips_dir = os.path.join(out_dir, "strips")
    stills_dir = os.path.join(out_dir, "stills")

    gifs = sorted(glob.glob(os.path.join(clips_dir, "*.gif")))
    strips = sorted(glob.glob(os.path.join(strips_dir, "*.png")))
    stills = sorted(glob.glob(os.path.join(stills_dir, "*.png")))

    parts: list[str] = []

    if gifs:
        parts.append("<h2>Нужные игре клипы — движение</h2><div class=\"clips\">")
        for path in gifs:
            name = os.path.splitext(os.path.basename(path))[0]
            size = os.path.getsize(path) // 1024
            parts.append(
                "<div class=\"clip\">"
                f"<img src=\"clips/{os.path.basename(path)}\" width=\"240\" alt=\"{name}\">"
                f"<div class=\"name\">{name}</div>"
                f"<div class=\"meta\">GIF, {size} КБ</div>"
                "</div>"
            )
        parts.append("</div>")

    if strips:
        parts.append("<h2>Раскадровка — все кадры клипа подряд</h2>")
        for path in strips:
            name = os.path.splitext(os.path.basename(path))[0]
            parts.append(
                f"<div class=\"clip\" style=\"margin-bottom:14px\">"
                f"<div class=\"name\">{name}</div>"
                f"<div class=\"strip\"><img src=\"strips/{os.path.basename(path)}\" alt=\"{name}\"></div>"
                "</div>"
            )

    if stills:
        parts.append("<h2>Весь каталог — по одному кадру с каждого клипа</h2>")
        sheet = os.path.join(out_dir, "sheet.png")
        if os.path.isfile(sheet):
            parts.append("<div class=\"clip\"><img src=\"sheet.png\" style=\"max-width:100%\" alt=\"sheet\"></div>")

    body = "\n".join(parts) if parts else "<p>Кадров нет: сначала запустите ClipContactSheet.</p>"
    subtitle = (
        f"Кадров клипов: {len(gifs)} · раскадровок: {len(strips)} · "
        f"клипов в каталоге: {len(stills)}"
    )

    html = TEMPLATE.format(title=title, subtitle=subtitle, body=body)
    target = os.path.join(out_dir, "index.html")
    with open(target, "w", encoding="utf8", newline="\n") as handle:
        handle.write(html)
    return target


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("out", help="папка out от ClipContactSheet")
    parser.add_argument("--title", default="Клипы анимаций")
    args = parser.parse_args()

    if not os.path.isdir(args.out):
        print("ERROR: нет папки: " + args.out)
        return 2

    target = build_gallery(args.out, args.title)
    print("gallery=" + target)
    return 0


if __name__ == "__main__":
    sys.exit(main())
