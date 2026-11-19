#!/usr/bin/env python3
"""Единая точка входа для всех проверок экономии контекста.

Зачем
-----
Меры экономии бесполезны, если их надо помнить и запускать руками. Этот скрипт
делает всё сразу и вызывается одной командой — его же вызывает агент перед
работой и после неё.

    python tools/tokens/check.py            # быстрая проверка (секунды)
    python tools/tokens/check.py --full     # + пересборка карточек и отчёт
    python tools/tokens/check.py --guard    # только предохранители, для хука

Что проверяется
---------------
1. Карточки подсистем на месте и не устарели относительно исходников.
2. Нет мусорных путей в правилах (obj/, Library/, Builds/) — они давали по
   12,5 тыс. токенов на один вывод команды.
3. В правилах есть обязательные директивы экономии.
4. Замеры стоимости задач сохранены (docs/tokens-benchmarks.json).

Код возврата 0 — всё в порядке, 1 — есть проблемы (печатаются).
"""

from __future__ import annotations

import argparse
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CARDS = os.path.join(ROOT, "docs", "cards")
AGENTS = os.path.join(ROOT, "AGENTS.md")
LEDGER = os.path.join(ROOT, "docs", "warrior-animation-ledger.md")
BENCH = os.path.join(ROOT, "docs", "tokens-benchmarks.json")
RULES = os.path.join(ROOT, "docs", "rules.md")
# Файл, который DSH НЕ загружает: кандидаты загрузчика — AGENTS.md, CLAUDE.md
# (и .local-оверлеи). Всё, что написано в AGENT.md, для агента не существует.
DEAD_RULES = os.path.join(ROOT, "AGENT.md")

# Пути, которые обязаны быть исключены из любого поиска и вывода. Появление
# любого из них в команде — прямой путь к 12,5 тыс. токенов мусора в контексте.
NOISE = ("obj/", "obj\\", "Library/", "Library\\", "Builds/", "Builds\\",
         "node_modules", ".git/")

# Директивы, которые обязаны присутствовать в правилах. Без них новая сессия
# не узнает о дисциплине, и всё вернётся к прежнему расходу.
REQUIRED_RULES = [
    "tools/tokens/check.py",
    "docs/cards/",
    "Select-Object -First",
    "grep",
    "offset",
    # Запреты проверяются скриптом, а не памятью агента: директива обязана
    # присутствовать в правилах, иначе про скрипт никто не узнает.
    "tools/check-rules.ps1",
    "docs/rules.md",
    # Карта ассетов и её документ: без ссылки в правилах новая сессия снова
    # начнёт вручную перебирать GUID и меты, как это уже было.
    "docs/unity-assets.md",
    "tools/unity/asset-map.py",
]

# Что обязано быть в docs/rules.md — файле, который читается по требованию.
REQUIRED_IN_RULES = [
    "Definition of Done",
    "Работа с тестами",
    "Работа с сетью",
]

problems: list[str] = []
notes: list[str] = []


def check_cards() -> None:
    if not os.path.isdir(CARDS):
        problems.append("Нет папки docs/cards — карточки подсистем не созданы.")
        return

    cards = [f for f in os.listdir(CARDS) if f.endswith(".md")]
    if len(cards) < 8:
        problems.append(f"Карточек всего {len(cards)} — ожидалось не меньше 8.")

    stale = []
    for name in cards:
        path = os.path.join(CARDS, name)
        if os.path.getsize(path) < 400:
            problems.append(f"Карточка {name} подозрительно мала "
                            f"({os.path.getsize(path)} Б) — вероятно, без API.")
            continue
        with open(path, encoding="utf-8") as fh:
            head = fh.read(600)
        # В карточке путь исходника записан как `...`
        for token in head.split("`"):
            if token.endswith(".cs") and "/" in token:
                src = os.path.join(ROOT, token.replace("/", os.sep))
                if os.path.exists(src) and os.path.getmtime(src) > os.path.getmtime(path):
                    stale.append(name)
                break
    if stale:
        notes.append("Устарели (исходник новее карточки): " + ", ".join(sorted(set(stale)))
                     + " — пересоберите: python tools/tokens/make_cards.py")

    total_src = 0
    total_card = 0
    for name in cards:
        path = os.path.join(CARDS, name)
        total_card += os.path.getsize(path) // 4
        with open(path, encoding="utf-8") as fh:
            head = fh.read(600)
        for token in head.split("`"):
            if token.endswith(".cs") and "/" in token:
                src = os.path.join(ROOT, token.replace("/", os.sep))
                if os.path.exists(src):
                    total_src += os.path.getsize(src) // 4
                break
    if total_src and total_card:
        ratio = 100 * (1 - total_card / total_src)
        notes.append(f"Карточки: {total_card:,} токенов против {total_src:,} "
                     f"в исходниках — экономия {ratio:.0f}% на чтение.".replace(",", " "))


def check_rules() -> None:
    if not os.path.exists(AGENTS):
        problems.append("Нет AGENTS.md — правила не подхватятся в новой сессии.")
        return
    with open(AGENTS, encoding="utf-8") as fh:
        text = fh.read()

    for rule in REQUIRED_RULES:
        if rule not in text:
            problems.append(f"В AGENTS.md нет директивы экономии: «{rule}»")

    # ЗАЩИТА ОТ ТИХОЙ ПОТЕРИ ПРАВИЛ.
    # `trail` перезаписывает всё между <!-- trail:start --> и <!-- trail:end -->.
    # Если раздел экономии попадёт внутрь этого блока, следующая регенерация
    # сотрёт его без единого сообщения — и расход вернётся к прежнему, а причина
    # останется незаметной. Проверяем, что заголовок экономии лежит ВНЕ блока.
    start = text.find("<!-- trail:start -->")
    end = text.find("<!-- trail:end -->")
    heading = text.find("ЭКОНОМИЯ КОНТЕКСТА")
    if heading == -1:
        problems.append("В AGENTS.md нет заголовка «ЭКОНОМИЯ КОНТЕКСТА».")
    elif start != -1 and end != -1 and start < heading < end:
        problems.append(
            "Раздел «ЭКОНОМИЯ КОНТЕКСТА» находится ВНУТРИ блока trail:start/end — "
            "`trail build` сотрёт его. Вынесите раздел выше маркера trail:start."
        )

    starts = text.count("<!-- trail:start -->")
    ends = text.count("<!-- trail:end -->")
    if starts != ends:
        problems.append(f"Маркеры trail непарные: start={starts}, end={ends}.")

    # ЗАЩИТА ОТ ВОЗВРАТА МЁРТВОГО ФАЙЛА ПРАВИЛ.
    # В репозитории когда-то жил AGENT.md (11 КБ правил). DSH читает только
    # AGENTS.md и CLAUDE.md, поэтому все правила из AGENT.md молча не работали.
    # Файл удалён; если он появится снова — правила снова станут мёртвыми, и
    # заметить это будет невозможно. Падаем явно.
    if os.path.exists(DEAD_RULES):
        problems.append(
            "В корне снова появился AGENT.md — DSH его НЕ загружает (кандидаты: "
            "AGENTS.md, CLAUDE.md). Правила из него не работают. Переносите "
            "в AGENTS.md или docs/rules.md и удалите файл."
        )

    # docs/rules.md читается по требованию — он обязан существовать и содержать
    # разделы, ради которых его вообще открывают.
    if not os.path.exists(RULES):
        problems.append("Нет docs/rules.md — процессные правила проекта потеряны.")
    else:
        with open(RULES, encoding="utf-8") as fh:
            rules_text = fh.read()
        for section in REQUIRED_IN_RULES:
            if section not in rules_text:
                problems.append(f"В docs/rules.md нет раздела «{section}».")

    if not os.path.exists(LEDGER):
        problems.append("Нет docs/warrior-animation-ledger.md — "
                        "журнал доказанных решений потерян.")

    for noise in NOISE:
        marker = f"`*{noise}*`"
        if marker in text:
            notes.append(f"В правилах упомянут шумный путь {noise} — ок, если это запрет.")


def check_benchmarks() -> None:
    if not os.path.exists(BENCH):
        notes.append("Замеров стоимости задач ещё нет. "
                     "Первый: python tools/tokens/benchmark.py mark \"<маркер>\"")
        return
    try:
        with open(BENCH, encoding="utf-8") as fh:
            rows = json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        problems.append(f"Файл замеров повреждён: {exc}")
        return
    if not rows:
        notes.append("Файл замеров пуст.")
        return
    last = rows[-1]
    notes.append(f"Замеров: {len(rows)}. Последний: «{last.get('label')}» — "
                 f"{last.get('steps')} шагов, "
                 f"{last.get('avgContext', 0):,} токенов контекста на шаг."
                 .replace(",", " "))


def main() -> None:
    ap = argparse.ArgumentParser(description="Проверка экономии контекста.")
    ap.add_argument("--full", action="store_true",
                    help="пересобрать карточки и напечатать полный отчёт")
    ap.add_argument("--guard", action="store_true",
                    help="только предохранители (для хука), без отчёта")
    opts = ap.parse_args()

    if opts.full:
        import subprocess
        subprocess.run([sys.executable, os.path.join(ROOT, "tools", "tokens", "make_cards.py")],
                       cwd=ROOT)
        subprocess.run([sys.executable, os.path.join(ROOT, "tools", "tokens", "report.py"),
                        "--simulate"], cwd=ROOT)

    check_cards()
    check_rules()
    check_benchmarks()

    if opts.guard:
        for p in problems:
            print(f"ЭКОНОМИЯ КОНТЕКСТА: {p}", file=sys.stderr)
        sys.exit(1 if problems else 0)

    print("=" * 78)
    print("ПРОВЕРКА ЭКОНОМИИ КОНТЕКСТА")
    print("=" * 78)
    for n in notes:
        print(f"  · {n}")
    if problems:
        print()
        for p in problems:
            print(f"  ✗ {p}")
        print()
        print(f"Проблем: {len(problems)}. Исправьте и запустите снова.")
        sys.exit(1)

    print()
    print("  ✓ Всё на месте. Правила, карточки и замеры в порядке.")


if __name__ == "__main__":
    main()
