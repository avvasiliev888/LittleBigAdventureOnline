# Unity-проект (клиент)

Здесь живёт Unity-клиент. Правила (ADR-001, ADR-006):

- авторитетной игровой логики в `MonoBehaviour` нет — только отображение и ввод;
- `Simulation.Core` подключается к клиенту **только** через `Bootstrap` и `Prediction`;
- `*.meta` трекаются git, бинарные ассеты > 100 KB — через Git LFS.

Версия редактора фиксируется в `ProjectSettings/ProjectVersion.txt` (Unity 6.3 LTS, 6000.3.x).
