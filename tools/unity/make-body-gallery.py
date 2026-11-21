#!/usr/bin/env python3
"""Галерея тел: выбрать подходящие модели глазами.

Зачем: имя файла не говорит о силуэте. «Base Man» и «Fantasy_character» —
это два разных решения по стилю, и принять его можно только увидев рядом.
Одна страница даёт сравнение, в котором выбор действительно возможен.

Вход — папка с PNG от `Albion.Editor.BodyGallery` и её манифест `bodies.json`.
Тела и части показываются раздельно: часть тела без рига не может быть мобом,
но заказчик вправе видеть, что она вообще есть.

Использование:
    python tools/unity/make-body-gallery.py <папка> [--title "..."]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

TEMPLATE = """<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<title>{title}</title>
<style>
  body {{ background:#15171c; color:#dfe3e8; font:14px/1.5 "Segoe UI",Arial,sans-serif;
         margin:0; padding:26px 30px 60px; }}
  h1 {{ font-size:20px; font-weight:600; margin:0 0 4px; }}
  .sub {{ color:#8b93a0; margin-bottom:22px; }}
  h2 {{ font-size:14px; font-weight:600; margin:30px 0 12px; color:#aab3c0;
        text-transform:uppercase; letter-spacing:.07em; }}
  .grid {{ display:flex; flex-wrap:wrap; gap:14px; }}
  .card {{ background:#1d2027; border:1px solid #2a2f39; border-radius:8px; padding:9px;
           width:236px; }}
  .card img {{ display:block; width:100%; border-radius:4px; background:#22262e; }}
  .card .n {{ margin-top:7px; font-weight:600; font-size:13px; }}
  .card .p {{ color:#6f7784; font-size:11px; word-break:break-all; margin-top:2px; }}
  .tag {{ display:inline-block; font-size:10px; padding:1px 6px; border-radius:9px;
          margin-top:5px; letter-spacing:.04em; }}
  .humanoid {{ background:#1f3a2a; color:#8ee0a8; }}
  .generic {{ background:#3a3320; color:#e8d08e; }}
  .part {{ background:#2c2f38; color:#9aa4b2; }}
  details {{ margin-top:10px; }}
  summary {{ cursor:pointer; color:#8b93a0; font-size:13px; }}
</style>
</head>
<body>
<h1>{title}</h1>
<div class="sub">{subtitle}</div>
{body}
</body>
</html>
"""


def card(entry: dict, folder: str, tag_class: str, tag_text: str) -> str:
    name = os.path.splitext(entry["file"])[0]
    path = entry["path"]
    # Only the last two path segments: the full asset path is noise next to the picture.
    short = "/".join(path.split("/")[-2:])
    # A warning is a badge, not a hidden body: a model with no material is repairable in minutes,
    # while dropping it from the list would remove a usable mesh from the choice silently.
    note = entry.get("note") or ""
    warn = f'<span class="tag warn">{note}</span>' if note else ""
    return (
        f'<div class="card"><img src="{folder}/{entry["file"]}.png" alt="{name}" loading="lazy">'
        f'<div class="n">{name}</div>'
        f'<div class="p">{short}</div>'
        f'<span class="tag {tag_class}">{tag_text}</span>{warn}</div>'
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("folder", help="папка с PNG от BodyGallery")
    parser.add_argument("--title", default="Тела из ассетов")
    args = parser.parse_args()

    manifest_path = os.path.join(args.folder, "bodies.json")
    if not os.path.isfile(manifest_path):
        print("ERROR: нет манифеста: " + manifest_path)
        return 2

    with open(manifest_path, encoding="utf8") as handle:
        data = json.load(handle)

    bodies = data.get("bodies", [])
    parts = data.get("parts", [])

    humanoid = [b for b in bodies if b.get("rig") == "humanoid"]
    generic = [b for b in bodies if b.get("rig") != "humanoid"]

    chunks: list[str] = []

    if humanoid:
        chunks.append(f"<h2>Humanoid — подходят для общих наборов анимаций ({len(humanoid)})</h2>")
        chunks.append('<div class="grid">')
        for entry in humanoid:
            chunks.append(card(entry, ".", "humanoid", "humanoid"))
        chunks.append("</div>")

    if generic:
        chunks.append(f"<h2>Generic — только собственные клипы ({len(generic)})</h2>")
        chunks.append('<div class="grid">')
        for entry in generic:
            chunks.append(card(entry, ".", "generic", "generic"))
        chunks.append("</div>")

    if parts:
        chunks.append("<details><summary>")
        chunks.append(f"Части и предметы без рига — {len(parts)} (развернуть)</summary>")
        chunks.append('<div class="grid" style="margin-top:12px">')
        for entry in parts:
            chunks.append(card(entry, ".", "part", "часть"))
        chunks.append("</div></details>")

    subtitle = (
        f"Humanoid: {len(humanoid)} · Generic: {len(generic)} · "
        f"частей: {len(parts)}. Снято в T-позе, без одежды и оружия — "
        "сравнивается сама модель."
    )

    html = TEMPLATE.format(title=args.title, subtitle=subtitle, body="\n".join(chunks))
    target = os.path.join(args.folder, "index.html")
    with open(target, "w", encoding="utf8", newline="\n") as handle:
        handle.write(html)

    print("gallery=" + target)
    print(f"humanoid={len(humanoid)} generic={len(generic)} parts={len(parts)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())