#!/usr/bin/env python3
"""Замер стоимости одной задачи: сколько токенов ушло между двумя метками.

Зачем
-----
`report.py` показывает расход по сессиям целиком, но им нельзя доказать эффект
меры: «до» и «после» оказываются в разных сессиях с разной историей, и цифры
несравнимы. Этот скрипт измеряет ровно одну ЗАДАЧУ (один ход пользователя и
всё, что агент сделал в ответ) и печатает сопоставимую строку.

Работает так: находит в логах сессий ход, содержащий заданный маркер
(подстроку в сообщении пользователя или в названии задачи), и считает по нему:

    шагов      — сколько раз агент обращался к модели
    вх.контекст — средний контекст на шаге (главная метрика)
    токенов    — фактический расход
    инструм.   — сколько контекста дали результаты инструментов

Использование
-------------
    # записать замер текущей задачи (маркер задаётся вручную)
    python tools/tokens/benchmark.py mark "эталон: правка Idle"

    # посмотреть все сделанные замеры
    python tools/tokens/benchmark.py list

    # сравнить два замера
    python tools/tokens/benchmark.py compare 1 2

Замеры складываются в docs/tokens-benchmarks.json — файл коммитится, чтобы
цифры сохранялись между машинами и сессиями.
"""

from __future__ import annotations

import argparse
import collections
import glob
import json
import os
import sys
from dataclasses import dataclass

try:
    import zstandard
except ImportError:  # pragma: no cover
    sys.exit("Нужен пакет zstandard:  python -m pip install zstandard")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SESSIONS_ROOT = os.path.expanduser(r"~\.dsh\sessions")
STORE = os.path.join(ROOT, "docs", "tokens-benchmarks.json")
CHARS_PER_TOKEN = 4.0


@dataclass
class TurnCost:
    marker: str
    session: str
    turn: int
    steps: int
    input_tokens: int
    output_tokens: int
    cache_read: int
    total: int
    tool_tokens: dict

    @property
    def avg_context(self) -> float:
        return (self.input_tokens + self.cache_read) / max(self.steps, 1)


def project_sessions() -> list[str]:
    cwd_name = ROOT.replace("\\", "-").replace("/", "-").replace(":", "")
    pattern = os.path.join(SESSIONS_ROOT, f"--{cwd_name}--", "**", "session.v4.jsonl.zstd")
    return glob.glob(pattern, recursive=True)


def iter_records(path: str):
    dctx = zstandard.ZstdDecompressor()
    with open(path, "rb") as fh:
        raw = dctx.stream_reader(fh).read()
    for line in raw.split(b"\n"):
        if not line.strip():
            continue
        try:
            yield json.loads(line)
        except json.JSONDecodeError:
            continue


def user_text(rec: dict) -> str:
    out = []
    for c in (rec.get("data") or {}).get("content") or []:
        if c.get("type") == "text":
            out.append(c.get("text") or "")
    return "\n".join(out)


def text_size(msg: dict | None) -> int:
    n = 0
    for c in (msg or {}).get("content") or []:
        t = c.get("type")
        if t in ("text", "reasoning"):
            n += len(c.get("text") or "")
        elif t == "image":
            n += 2000 * CHARS_PER_TOKEN
        else:
            n += len(json.dumps(c))
    return n


def measure(marker: str) -> TurnCost | None:
    """Находит ход, где встречается маркер, и считает его стоимость.

    Тонкость: у записи `user/message` НЕТ поля `turn` — оно есть только у
    `agent/inbox/spliced`, который её доставил, и у `turn/start`. Поэтому номер
    хода приходится брать не из самого сообщения, а из ближайшего следующего
    `turn/start` (или из `spliced`). Сопоставление «сообщение → ход» по самому
    сообщению молча даёт None, и замер не находится — эта ошибка уже была.
    """
    names: dict[str, str] = {}
    found_turn = None
    found_session = None

    def turn_of(rec: dict) -> int | None:
        data = rec.get("data") or {}
        if "turn" in data:
            return data.get("turn")
        for item in data.get("inserted") or []:
            if isinstance(item, dict) and "turn" in item:
                return item.get("turn")
        return None

    for path in project_sessions():
        session = os.path.basename(os.path.dirname(path))
        pending = False
        for rec in iter_records(path):
            kind = rec.get("type")
            if kind == "tool/call":
                names[rec["data"].get("callId")] = rec["data"].get("name", "?")
            elif kind == "user/message":
                # ВАЖНО: не перезаписывать флаг. После совпадения в том же ходе идут
                # другие user/message (контекст, повторные вставки), и присваивание
                # `pending = ...` сбрасывало уже найденное совпадение на False —
                # замер молча не находился. Только накопление через `or`.
                if marker.lower() in user_text(rec).lower():
                    pending = True
            elif kind == "turn/start" and pending:
                # turn/start приходит ПОСЛЕ agent/inbox/spliced и несёт номер хода.
                found_turn = (rec.get("data") or {}).get("turn")
                found_session = session
                break
            elif kind == "agent/inbox/spliced" and pending:
                # spliced идёт раньше и номера хода обычно не содержит — не выходим
                # из цикла, ждём turn/start.
                t = turn_of(rec)
                if t is not None:
                    found_turn = t
                    found_session = session
                    break
        if found_turn is None:
            continue
        steps = inp = out = cache = total = 0
        tools = collections.Counter()
        for rec in iter_records(path):
            kind = rec.get("type")
            data = rec.get("data") or {}
            if data.get("turn") != found_turn:
                continue
            if kind == "assistant/message":
                usage = data.get("usage") or {}
                steps += 1
                inp += usage.get("inputTokens", 0)
                out += usage.get("outputTokens", 0)
                cache += usage.get("cacheReadTokens", 0)
                total += usage.get("totalTokens", 0)
            elif kind == "tool/result":
                msg = data.get("message") or {}
                tool = names.get(msg.get("toolCallId"), "?")
                tools[tool] += int(text_size(msg) / CHARS_PER_TOKEN)

        if steps == 0:
            return None
        return TurnCost(marker, found_session, found_turn, steps, inp, out,
                        cache, total, dict(tools))
    return None


def load_store() -> list:
    if not os.path.exists(STORE):
        return []
    with open(STORE, encoding="utf-8") as fh:
        return json.load(fh)


def save_store(rows: list) -> None:
    os.makedirs(os.path.dirname(STORE), exist_ok=True)
    with open(STORE, "w", encoding="utf-8") as fh:
        json.dump(rows, fh, ensure_ascii=False, indent=2)


def cmd_mark(marker: str, label: str) -> None:
    cost = measure(marker)
    if cost is None:
        sys.exit(f"Ход с маркером «{marker}» не найден. "
                 "Маркер должен встречаться в сообщении пользователя.")

    rows = load_store()
    entry = {
        "label": label or marker,
        "marker": marker,
        "session": cost.session,
        "turn": cost.turn,
        "steps": cost.steps,
        "inputTokens": cost.input_tokens,
        "outputTokens": cost.output_tokens,
        "cacheReadTokens": cost.cache_read,
        "totalTokens": cost.total,
        "avgContext": round(cost.avg_context),
        "toolTokens": cost.tool_tokens,
    }
    rows.append(entry)
    save_store(rows)

    print(f"Замер #{len(rows)}: {entry['label']}")
    print(f"  сессия {cost.session[:18]}  ход {cost.turn}")
    print(f"  шагов={cost.steps}  ср.контекст={cost.avg_context:,.0f}".replace(",", " "))
    print(f"  токенов={cost.total:,}".replace(",", " "))
    print(f"  сохранено в {os.path.relpath(STORE, ROOT)}")


def cmd_list() -> None:
    rows = load_store()
    if not rows:
        print("Замеров пока нет. Сделайте первый: benchmark.py mark \"<маркер>\"")
        return
    print(f"{'#':>3} {'замер':34}{'шагов':>7}{'ср.контекст':>13}{'токенов':>15}")
    print("-" * 74)
    for i, r in enumerate(rows, 1):
        print(f"{i:>3} {r['label'][:34]:34}{r['steps']:>7}"
              f"{r['avgContext']:>13,}{r['totalTokens']:>15,}".replace(",", " "))


def cmd_compare(a: int, b: int) -> None:
    rows = load_store()
    if not (1 <= a <= len(rows) and 1 <= b <= len(rows)):
        sys.exit(f"Есть только замеры 1..{len(rows)}.")
    x, y = rows[a - 1], rows[b - 1]
    print(f"Сравнение: #{a} «{x['label']}»  ->  #{b} «{y['label']}»")
    print(f"{'метрика':18}{'было':>16}{'стало':>16}{'изменение':>13}")
    print("-" * 64)
    for key, name in (("steps", "шагов"), ("avgContext", "ср. контекст"),
                      ("totalTokens", "токенов")):
        vx, vy = x[key], y[key]
        delta = 100 * (vy - vx) / vx if vx else 0
        print(f"{name:18}{vx:>16,}{vy:>16,}{delta:>12.0f}%".replace(",", " "))

    tx, ty = x.get("toolTokens") or {}, y.get("toolTokens") or {}
    keys = sorted(set(tx) | set(ty), key=lambda k: -(tx.get(k, 0) + ty.get(k, 0)))[:8]
    if keys:
        print()
        print("По инструментам:")
        print(f"{'инструмент':20}{'было':>14}{'стало':>14}")
        for k in keys:
            print(f"{k:20}{tx.get(k,0):>14,}{ty.get(k,0):>14,}".replace(",", " "))


def main() -> None:
    ap = argparse.ArgumentParser(description="Замер стоимости одной задачи.")
    sub = ap.add_subparsers(dest="cmd", required=True)

    m = sub.add_parser("mark", help="записать замер по маркеру")
    m.add_argument("marker", help="подстрока в сообщении пользователя")
    m.add_argument("--label", default="", help="человеческое имя замера")

    sub.add_parser("list", help="показать все замеры")

    cmp_ = sub.add_parser("compare", help="сравнить два замера")
    cmp_.add_argument("a", type=int)
    cmp_.add_argument("b", type=int)

    opts = ap.parse_args()
    if opts.cmd == "mark":
        cmd_mark(opts.marker, opts.label)
    elif opts.cmd == "list":
        cmd_list()
    else:
        cmd_compare(opts.a, opts.b)


if __name__ == "__main__":
    main()
