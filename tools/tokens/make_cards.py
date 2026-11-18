#!/usr/bin/env python3
"""Генератор карточек подсистем: сжатая замена чтению исходников.

Принцип
-------
Один раз записать знание дешевле, чем каждый раз его вычитывать. Замер по
10 сессиям проекта показал: `read` съел 49,7% всего контекста инструментов
(3,4 млн токенов), причём 38% чтений — повторные. `Simulation.cs` прочитан
177 раз, `ConfigLoader.cs` — 153 раза. Каждое чтение — ~1000 токенов,
и они перечитываются на всех последующих шагах.

Карточка — это ~60-100 строк: что за подсистема, её публичный API, инварианты
и грабли. Она заменяет чтение 5-13 тыс. токенов исходника.

Откуда берётся содержимое
-------------------------
Не из рассуждений модели, а детерминированно из двух источников:
  1. граф `trail` (skeleton каждого файла) — сигнатуры, $0;
  2. заголовочные doc-комментарии в самом коде.
Поэтому пересборка карточек не стоит токенов и не устаревает молча.

Использование
-------------
    python tools/tokens/make_cards.py            # пересобрать все карточки
    python tools/tokens/make_cards.py --check    # только проверить свежесть
    python tools/tokens/make_cards.py UnityView  # одна подсистема

Результат: docs/cards/<Имя>.md, коммитится в git.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CARDS = os.path.join(ROOT, "docs", "cards")

# Подсистемы, которые дороже всего читать целиком (по замеру повторных чтений).
# Порядок — по убыванию пользы: сначала те, что читались чаще всего.
TARGETS = [
    ("Simulation", "src/Simulation.Core/Simulation.cs",
     "Ядро симуляции: тик 20 Гц, движение, бой, смерть/респавн, снапшоты."),
    ("ConfigLoader", "src/Shared/ConfigLoader.cs",
     "Загрузка и валидация JSON-конфигов (ADR-005)."),
    ("SimulationConfig", "src/Simulation.Core/SimulationConfig.cs",
     "Типизированный доступ к конфигам симуляции."),
    ("EntityState", "src/Simulation.Core/EntityState.cs",
     "Состояние сущности: позиция, HP, инвентарь, кулдауны."),
    ("ClientPipeline", "src/Client.Network/ClientPipeline.cs",
     "Клиентский конвейер: 20 Гц тик, ввод, предсказание, рендер."),
    ("ClientPrediction", "src/Client.Network/ClientPrediction.cs",
     "Предсказание и реконсиляция локального игрока (ADR-006)."),
    ("UnityView", "unity-client/Assets/Scripts/View/UnityView.cs",
     "Отрисовка: интерполяция снапшотов, скорость для анимации, оружие."),
    ("CharacterVisuals", "unity-client/Assets/Scripts/View/CharacterVisuals.cs",
     "Анимация воина: состояния, гистерезис, экипировка."),
    ("ClientRuntime", "unity-client/Assets/Scripts/Bootstrap/ClientRuntime.cs",
     "Точка сборки клиента: ввод, конвейер, HUD, камера."),
    ("UnityInputSource", "unity-client/Assets/Scripts/Input/UnityInputSource.cs",
     "Ввод: WASD, клик-к-цели, способность, сброс при смерти/респавне."),
    ("InputValidator", "src/Server/InputValidator.cs",
     "Серверная валидация команд: скорость, кулдаун, дистанция."),
    ("GameServer", "src/Server/GameServer.cs",
     "Серверный цикл: приём команд, тик, рассылка снапшотов."),
]


def _trail_command() -> list[str]:
    """Как запустить trail на этой машине.

    На Windows `trail` — это .cmd-обёртка, а subprocess без shell такие файлы
    не запускает (WinError 193 / FileNotFoundError). Поэтому либо запускаем
    node с точкой входа напрямую, либо просим shell. Тихое падение здесь уже
    привело к тому, что первые карточки вышли без API, но с нулевой экономией
    в отчёте — то есть ошибка была не видна.
    """
    node = shutil.which("node")
    entry = os.path.expanduser(
        r"~\AppData\Roaming\npm\node_modules\@trailhq\trail-code\dist\bin\trail.js"
    )
    if node and os.path.exists(entry):
        return [node, entry]
    found = shutil.which("trail")
    if found:
        return [found]
    return []


def run_trail_skeleton(rel_path: str) -> str:
    """Сигнатуры файла из графа trail. Детерминированно, без LLM.

    Тонкость: trail печатает список сигнатур в stdout, а служебные строки
    (обновление графа, счётчик токенов, предупреждение о certs) — в stderr.
    Объединяем оба потока и отфильтровываем служебное.
    """
    cmd = _trail_command()
    if not cmd:
        return ""
    try:
        out = subprocess.run(
            cmd + ["skeleton", rel_path],
            cwd=ROOT, capture_output=True, text=True, timeout=180,
            shell=cmd[0].lower().endswith((".cmd", ".bat")),
        )
    except (OSError, subprocess.SubprocessError) as exc:
        print(f"  [trail недоступен: {exc}]", file=sys.stderr)
        return ""

    combined = (out.stdout or "") + "\n" + (out.stderr or "")
    keep = []
    for line in combined.splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        if stripped.startswith("[trail]") or stripped.startswith("Warning:"):
            continue
        if stripped.startswith("trail skeleton"):
            continue
        keep.append(line.rstrip())

    return "\n".join(keep)


def extract_summary(abs_path: str) -> str:
    """Первый осмысленный doc-комментарий файла — его назначение."""
    try:
        with open(abs_path, encoding="utf-8", errors="replace") as fh:
            head = fh.read(6000)
    except OSError:
        return ""

    m = re.search(r"<summary>(.*?)</summary>", head, re.S)
    if m:
        text = re.sub(r"\s*\n\s*///?\s*", " ", m.group(1))
        text = re.sub(r"\s+", " ", text).strip()
        return text
    m = re.search(r'"""(.+?)"""', head, re.S)
    if m:
        return re.sub(r"\s+", " ", m.group(1)).strip()
    return ""


def count_lines(abs_path: str) -> int:
    try:
        with open(abs_path, encoding="utf-8", errors="replace") as fh:
            return sum(1 for _ in fh)
    except OSError:
        return 0


def build_card(name: str, rel_path: str, purpose: str) -> str:
    abs_path = os.path.join(ROOT, rel_path.replace("/", os.sep))
    lines = count_lines(abs_path)
    approx_tokens = int(os.path.getsize(abs_path) / 4) if os.path.exists(abs_path) else 0
    summary = extract_summary(abs_path)
    skeleton = run_trail_skeleton(rel_path)

    body = [
        f"# {name}",
        "",
        f"**Файл:** `{rel_path}`  ",
        f"**Размер:** {lines} строк ≈ {approx_tokens:,} токенов — ".replace(",", " ")
        + "читать целиком дорого, пользуйтесь этой карточкой.",
        "",
        f"**Назначение:** {purpose}",
        "",
    ]
    if summary and summary != purpose:
        body += ["**Из кода:** " + summary, ""]

    if skeleton:
        body += ["## API (детерминированно из графа trail)", "", "```", skeleton.strip(), "```", ""]
    else:
        body += [
            "## API",
            "",
            "_Граф trail недоступен. Обновите: `trail build`, затем пересоберите_",
            "_карточки: `python tools/tokens/make_cards.py`._",
            "",
        ]

    body += [
        "## Как пользоваться",
        "",
        f"1. Нужен ответ про {name} — читайте здесь, а не {rel_path}.",
        "2. Нужна деталь конкретного метода — `trail ask \"<вопрос>\" --source`",
        "   или `read` с `offset`/`limit` вокруг нужной строки.",
        "3. Правка кода — обычным образом; после крупных изменений пересоберите",
        "   карточку: `python tools/tokens/make_cards.py " + name + "`.",
        "",
    ]
    return "\n".join(body)


def main() -> None:
    ap = argparse.ArgumentParser(description="Карточки подсистем вместо чтения исходников.")
    ap.add_argument("only", nargs="?", help="собрать только одну подсистему")
    ap.add_argument("--check", action="store_true",
                    help="проверить, что все карточки на месте и не пустые")
    opts = ap.parse_args()

    os.makedirs(CARDS, exist_ok=True)
    targets = [t for t in TARGETS if not opts.only or t[0].lower() == opts.only.lower()]
    if not targets:
        sys.exit(f"Нет такой подсистемы: {opts.only}. "
                 f"Есть: {', '.join(t[0] for t in TARGETS)}")

    if opts.check:
        missing = 0
        for name, rel, _ in TARGETS:
            path = os.path.join(CARDS, name + ".md")
            ok = os.path.exists(path) and os.path.getsize(path) > 400
            print(f"{'OK  ' if ok else 'НЕТ '} {name}: {os.path.getsize(path) if os.path.exists(path) else 0} байт")
            missing += 0 if ok else 1
        if missing:
            sys.exit(f"Отсутствуют или пусты: {missing}. Запустите без --check.")
        return

    total_saved = 0
    for name, rel, purpose in targets:
        text = build_card(name, rel, purpose)
        path = os.path.join(CARDS, name + ".md")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text)

        src = os.path.join(ROOT, rel.replace("/", os.sep))
        src_tokens = int(os.path.getsize(src) / 4) if os.path.exists(src) else 0
        card_tokens = int(len(text) / 4)
        saved = max(0, src_tokens - card_tokens)
        total_saved += saved
        print(f"{name:18} карточка={card_tokens:5,}  исходник={src_tokens:6,}  "
              f"экономия на чтение={saved:6,}".replace(",", " "))

    print()
    print(f"Экономия на ОДНО чтение каждой подсистемы: ~{total_saved:,} токенов".replace(",", " "))
    print(f"Сами файлы лежат в {os.path.relpath(CARDS, ROOT)}")


if __name__ == "__main__":
    main()
