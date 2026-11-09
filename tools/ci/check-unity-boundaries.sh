#!/usr/bin/env bash
# Границы Unity-клиента (M6): авторитетная логика не должна утекать в MonoBehaviour,
# а движение капсул — появляться помимо снапшотов.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY_SCRIPTS="$ROOT/unity-client/Assets/Scripts"
FAILED=0

fail() {
    echo "FAIL: $1"
    FAILED=1
}

if [ ! -d "$UNITY_SCRIPTS" ]; then
    echo "skip: unity-client/Assets/Scripts отсутствует"
    exit 0
fi

echo "== 1. Клиент не создаёт и не тикает симуляцию вне Bootstrap/Prediction =="
if grep -RInE --include='*.cs' 'new[[:space:]]+(CoreSimulation|Simulation|Simulation\.Core\.Simulation)[[:space:]]*\(' "$UNITY_SCRIPTS" | grep -v '/Bootstrap/' | grep -v '/Prediction/'; then
    fail "создание Simulation в Unity-клиенте вне Bootstrap/Prediction"
else
    echo "ok: Simulation не создаётся в клиентских скриптах"
fi

if grep -RInE --include='*.cs' '(simulation|Simulation)\.Tick[[:space:]]*\(' "$UNITY_SCRIPTS"; then
    fail "клиент вызывает Tick симуляции"
else
    echo "ok: Tick симуляции в клиенте не вызывается"
fi

echo "== 2. View/Input не двигают объекты сами (только позиция из снапшота) =="
if grep -RInE --include='*.cs' 'transform\.(position[[:space:]]*\+=|Translate\()' "$UNITY_SCRIPTS/View" "$UNITY_SCRIPTS/Input" 2>/dev/null; then
    fail "логика движения в View/Input (transform.position += / Translate)"
else
    echo "ok: прямого движения в View/Input нет"
fi

echo "== 3. Ввод не читается во View =="
if grep -RInE --include='*.cs' 'Input\.Get|Keyboard\.current' "$UNITY_SCRIPTS/View" 2>/dev/null; then
    fail "View читает ввод (ввод должен идти через IInputSource)"
else
    echo "ok: View не читает ввод"
fi

if [ "$FAILED" -ne 0 ]; then
    echo "Проверка границ Unity не пройдена."
    exit 1
fi

echo "Проверка границ Unity пройдена."
