#!/usr/bin/env bash
# Проверки архитектуры для CI (ADR-001). Запускается на ubuntu-latest и локально (Git Bash).
# 1. В Simulation.Core, Shared и Client.Network нет UnityEngine.
# 2. Simulation.Core не ссылается на другие проекты.
# 3. Simulation.Core не содержит запрещённых API времени/случайности (детерминизм, ADR-003/007).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"

failed=0

fail() {
  echo "FAIL: $*" >&2
  failed=1
}

echo "== 1. Запрет UnityEngine в чистых проектах =="
for dir in src/Simulation.Core src/Shared src/Client.Network; do
  if grep -rIn --include='*.cs' -E 'UnityEngine|using UnityEditor' "$dir"; then
    fail "в $dir найден UnityEngine/UnityEditor (ADR-001)"
  else
    echo "ok: $dir без Unity"
  fi
done

echo "== 2. Simulation.Core не зависит от других проектов =="
if grep -n '<ProjectReference' src/Simulation.Core/Simulation.Core.csproj; then
  fail "Simulation.Core содержит ProjectReference (ADR-001: зависит только от BCL)"
else
  echo "ok: ProjectReference отсутствует"
fi

echo "== 3. Детерминизм: запрещённые API в Simulation.Core =="
if grep -rIn --include='*.cs' -E 'DateTime\.(Now|UtcNow)|Guid\.NewGuid|Environment\.TickCount|Stopwatch|new Random\(' src/Simulation.Core; then
  fail "в Simulation.Core найден недетерминированный API (ADR-003/ADR-007)"
else
  echo "ok: недетерминированных API нет"
fi

echo "== 4. Запрет UnityEngine в служебных проектах агента =="
if grep -rIn --include='*.cs' 'UnityEngine' src/Server src/Tests; then
  fail "UnityEngine в Server/Tests"
else
  echo "ok: Server/Tests без Unity"
fi

if [ "$failed" -ne 0 ]; then
  echo "Архитектурные проверки провалены." >&2
  exit 1
fi

echo "Архитектурные проверки пройдены."
