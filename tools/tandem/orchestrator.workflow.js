// Оркестратор конвейера «Тандем».
//
// ЭТО НЕ САМОСТОЯТЕЛЬНЫЙ СКРИПТ. Это тело для инструмента workflow:
// агент читает файл, подставляет args и передаёт содержимое в workflow.
//
// ПОЧЕМУ ЧТЕНИЕ ПЛАНА И ГЕЙТЫ ВНУТРИ АГЕНТОВ, А НЕ ЗДЕСЬ:
// тело workflow не имеет ни файловой системы, ни сети, ни shell. Поэтому
// docs/plan_for_gemma.md читает вызывающий агент и передаёт задачи в args,
// а git/gates.ps1/build-windows.ps1 выполняют агенты (у них есть инструменты).
// Скрипт только раскладывает фазы и принимает решения по их структурированным ответам.
//
// args (всё обязательно, кроме отмеченного):
//   feature   : string            — имя фичи для отчёта
//   tasks     : [{id, title, body}] — задачи с "- [ ]" из доки плана (порядок сохраняется)
//   promptFiles : {architect, executor, reviewer, debugger} — ПУТИ к файлам ролей
//                 (агент-роль читает файл сам; текст в args не передаётся — экономия контекста)
//   model     : {planner:"deepseek-v4-flash", executor:"ois-gemma-4-26B"} (opt)
//   provider  : "openai" (opt)
//   maxAttempts : number (opt, по умолч. 3) — попыток исполнителя до эскалации
//   spend     : {calls:number} (opt) — сколько платных вызовов уже потрачено в этом месяце
//   capCalls  : number (opt, по умолч. 60) — жёсткий лимит платных вызовов
//   mode      : "tools" | "patch" (opt) — patch = gemma только генерирует патч
//
// Возврат: {done, stopped, reason, tasks:[...], decisions:[...], build, report}
//
// ИНВАРИАНТЫ (T-008, после разбора провала 22.11.2026):
//   1. Каждая задача ОБЯЗАНА попасть в `tasks` результата (нет пропусков).
//   2. ПК-билд запускается только при полном успехе: все задачи со статусом "pass".
//   3. `done` true только при том же условии — «успех» никогда не врёт.
//   4. Ревью-FAIL повторяет исполнителя (раньше `continue` уходил во внешний цикл
//      и задача молча выбрасывалась, а `done` оставался true).
//   5. Журнал решений по каждой задаче уходит в отчёт (`decisions`).

const cfg = args || {};
const tasks = (cfg.tasks || []).filter(t => t && t.id);
const plannerModel = (cfg.model && cfg.model.planner) || "deepseek-v4-flash";
const executorModel = (cfg.model && cfg.model.executor) || "ois-gemma-4-26B";
const provider = cfg.provider || "openai";
const maxAttempts = cfg.maxAttempts || 3;
const capCalls = cfg.capCalls || 60;
const mode = cfg.mode || "tools";

let paidCalls = (cfg.spend && cfg.spend.calls) || 0;
let executorCalls = 0;
// Журнал платных вызовов: уходит архитектору в промпт отчёта, чтобы в
// spend.jsonl попала строка НА КАЖДЫЙ вызов, а не только на те, что он вспомнил.
const callLog = [];
function paidCall(role, label) {
  paidCalls++;
  callLog.push({ n: paidCalls, role: role, label: label });
}

// Журнал решений (инвариант 5): по каждой задаче — попытка, вердикт, решение.
// Пишется агентом отчёта в docs/reports/logs/, потому что у скрипта нет fs.
const decisions = [];
function decide(taskId, text, extra) {
  const entry = Object.assign({ task: taskId, attempt: extra && extra.attempt, decision: text }, extra || {});
  decisions.push(entry);
  log(`${taskId}: ${text}`);
  return entry;
}

// Промпты ролей передаются ПУТЯМИ, а не текстом: вызывающий агент не обязан
// читать их целиком (экономия контекста), роль читает себя сама.
const promptFiles = cfg.promptFiles || {};
function rolePrompt(role) {
  const p = promptFiles[role];
  if (!p) return "";
  return `СНАЧАЛА прочитай инструментом read файл ${p} и следуй ему как инструкции своей роли. Это обязательно.`;
}

function budgetOk() {
  if (paidCalls >= capCalls) {
    log(`БЮДЖЕТ: платных вызовов ${paidCalls}/${capCalls} — остановка`);
    return false;
  }
  return true;
}

// --- схемы ответов: агенты обязаны вернуть ровно это ---
const EXEC_SCHEMA = {
  type: "object",
  properties: {
    status: { type: "string", enum: ["done", "blocked"] },
    branch: { type: "string" },
    summary: { type: "string" },
    changed_files: { type: "array", items: { type: "string" } },
    gates_ok: { type: "boolean" },
    failed_step: { type: "string" },
    log_tail: { type: "string" },
  },
  required: ["status", "changed_files", "gates_ok", "summary"],
  additionalProperties: false,
};

const REVIEW_SCHEMA = {
  type: "object",
  properties: {
    verdict: { type: "string", enum: ["PASS", "FAIL"] },
    must_fix: { type: "array", items: { type: "string" } },
    risk: { type: "string", enum: ["low", "medium", "high"] },
    notes: { type: "string" },
  },
  required: ["verdict", "risk", "notes", "must_fix"],
  additionalProperties: false,
};

const GATE_SCHEMA = {
  type: "object",
  properties: {
    ok: { type: "boolean" },
    failed_step: { type: "string" },
    tail: { type: "string" },
    unity_seconds: { type: "number" },
  },
  required: ["ok"],
  additionalProperties: false,
};

const BUILD_SCHEMA = {
  type: "object",
  properties: {
    ok: { type: "boolean" },
    exe_path: { type: "string" },
    error: { type: "string" },
  },
  required: ["ok"],
  additionalProperties: false,
};

function taskBlock(t) {
  return [
    `## ${t.id} — ${t.title || ""}`,
    t.body || "",
  ].join("\n");
}

function execPrompt(t, attempt, mustFix, diagnosis) {
  const parts = [
    rolePrompt("executor"),
    "Ты исполнитель конвейера «Тандем». Работай строго по задаче.",
    "",
    "=== ЗАДАЧА ===",
    taskBlock(t),
    "",
    `Ветка: agent/${t.id} (создай её от ${cfg.baseBranch || "main"}: git checkout ${cfg.baseBranch || "main"} && git checkout -b agent/${t.id}). Не пушь её.`,
    "Быстрые гейты: pwsh -File tools/tandem/gates.ps1 -Task " + t.id + " -SkipUnity",
    "Unity-гейт ты НЕ запускаешь — его запустит конвейер после ревью.",
  ];
  if (attempt > 1) {
    parts.push(
      "",
      "=== ПОПЫТКА " + attempt + " — СМЕНИ ПОДХОД ===",
      "Предложи 2 альтернативных способа решить задачу, выбери лучший и объясни почему в одну строку.",
      mustFix && mustFix.length ? "Замечания ревью, которые обязательно закрыть:\n- " + mustFix.join("\n- ") : "",
      diagnosis ? "Диагноз отладчика:\n" + diagnosis : ""
    );
  }
  parts.push(
    "",
    "=== ОТВЕТ ===",
    "Верни JSON по схеме. gates_ok=true только если pwsh-гейты прошли (build+test).",
    "Если задача требует файл вне списка «Файлы» — status=blocked, объясни в summary."
  );
  return parts.join("\n");
}

phase("План");
log(`Фича: ${cfg.feature || "без имени"}; задач: ${tasks.length}; режим: ${mode}; бюджет: ${paidCalls}/${capCalls}`);
if (!tasks.length) {
  // Пустой список — это не успех: иначе «всё прошло» без единой задачи (так и было 22.11.2026).
  log("ВНИМАНИЕ: список задач пуст — прогон считается неуспешным");
}

const results = [];
let stopped = null;
let reportText = "";
let buildInfo = null;

for (const t of tasks) {
  if (!budgetOk()) { stopped = "budget"; decide(t.id, "stop:budget"); break; }
  phase(`Задача ${t.id}`);

  let attempts = 0;
  let mustFix = [];
  let diagnosis = "";
  let lastExec = null;
  let lastReview = null;
  let settled = null;

  // ЦИКЛ ЗАДАЧИ: сюда возвращаются повторы после ревью и после провала гейтов.
  // Раньше ревью-FAIL делал `continue` вне этого цикла — и терял задачу целиком.
  while (!settled && !stopped) {
    if (attempts >= maxAttempts + 1) {
      if (lastReview && lastReview.verdict === "FAIL") {
        // --- отладчик ---
        if (!budgetOk()) { stopped = "budget"; decide(t.id, "stop:budget"); break; }
        paidCall("debugger", `${t.id} debugger`);
        phase(`Отладка ${t.id}`);
        log(`[${new Date(Date.now()).toISOString()}] start debugger ${t.id}`);
        const dbg = await agent(
          [
            rolePrompt("debugger"),
            "Ты отладчик конвейера «Тандем». Диагноз и план, ≤15 строк кода.",
            "",
            "=== ЗАДАЧА ===",
            taskBlock(t),
            "",
            "=== ИСПОЛНИТЕЛЬ ===",
            JSON.stringify(lastExec, null, 2),
            "",
            "=== РЕВЬЮ ===",
            JSON.stringify(lastReview, null, 2),
            "",
            "Прочитай полные логи из docs/reports/logs/ и релевантный код. Дай диагноз и план.",
          ].join("\n"),
          { model: plannerModel, provider: provider, label: `${t.id} debugger` }
        );
        log(`[${new Date(Date.now()).toISOString()}] done debugger ${t.id}`);
        diagnosis = dbg || "";
        settled = { status: "needs-human-debug", attempts: attempts, exec: lastExec, review: lastReview, diagnosis: diagnosis };
        stopped = "debug";
        decide(t.id, "stop:debug", { attempt: attempts });
        break;
      }

      settled = { status: "gate-fail", attempts: attempts, exec: lastExec };
      stopped = "gate-fail";
      decide(t.id, "stop:gate-fail", { attempt: attempts });
      break;
    }

    attempts++;
    log(`${t.id}: попытка ${attempts}`);

    log(`[${new Date(Date.now()).toISOString()}] start executor #${attempts} ${t.id}`);
    const exec = await agent(execPrompt(t, attempts, mustFix, diagnosis), {
      model: executorModel,
      provider: provider,
      label: `${t.id} executor #${attempts}`,
      schema: EXEC_SCHEMA,
    });
    log(`[${new Date(Date.now()).toISOString()}] done executor #${attempts} ${t.id}`);
    executorCalls++;

    if (!exec) {
      mustFix = ["исполнитель не вернул структурированный ответ"];
      decide(t.id, "retry:exec-null", { attempt: attempts });
      continue;
    }
    lastExec = exec;
    if (exec.status === "blocked") {
      settled = { status: "blocked", attempts: attempts, exec: lastExec };
      stopped = "blocked";
      decide(t.id, "stop:blocked", { attempt: attempts });
      break;
    }
    if (!exec.gates_ok) {
      mustFix = [`гейт «${exec.failed_step || "build"}» падает: ${(exec.log_tail || "").slice(-600)}`];
      decide(t.id, "retry:gates", { attempt: attempts, step: exec.failed_step || "build" });
      continue;
    }

    // --- ревью ---
    if (t.review === false || cfg.skipReview === true) {
      decide(t.id, "review:skipped");
      lastReview = { verdict: "PASS", must_fix: [], risk: "low", notes: "skipped by config" };
    } else {
      if (!budgetOk()) { stopped = "budget"; decide(t.id, "stop:budget"); break; }
      paidCall("reviewer", `${t.id} reviewer`);
      phase(`Ревью ${t.id}`);
      const review = await agent(
        [
          rolePrompt("reviewer"),
          "Ты ревьюер конвейера «Тандем». Ответ строго ОДНИМ JSON-сообщением.",
          "",
          "=== ЗАДАЧА ===",
          taskBlock(t),
          "",
          "=== ЧТО СДЕЛАЛ ИСПОЛНИТЕЛЬ ===",
          JSON.stringify(lastExec, null, 2),
          "",
          `Сам получи diff: git diff ${cfg.baseBranch || "main"} -- <файлы задачи> (или git diff --stat). Ветка сейчас agent/${t.id}.`,
          "ЗАПРЕТ: не запускать гейты, Unity и сборки. Читай только diff и логи.",
          "Если что-то не проверил — поставь verdict: \"PASS\", risk: \"medium\" и перечисли непроверенное в notes.",
          "Ответ — только JSON по схеме.",
        ].join("\n"),
        { model: plannerModel, provider: provider, label: `${t.id} reviewer`, schema: REVIEW_SCHEMA }
      );
      lastReview = review;

      if (!review) {
        settled = { status: "review-error", attempts: attempts, exec: lastExec };
        stopped = "review-error";
        decide(t.id, "stop:review-error", { attempt: attempts });
        break;
      }

      if (review.verdict === "FAIL") {
        mustFix = review.must_fix || [];
        decide(t.id, "retry:review-fail", { attempt: attempts, must_fix: mustFix });
        continue; // повтор исполнителя ВНУТРИ цикла задачи
      }
    }

    // --- PASS по ревью: полный гейт с Unity ---
    if (!budgetOk()) { stopped = "budget"; decide(t.id, "stop:budget"); break; }
    paidCall("gate", `${t.id} unity-gates`);
    phase(`Гейт Unity ${t.id}`);
    const full = await agent(
      [
        "Ты гейт-раннер конвейера «Тандем»: только запускаешь гейты и возвращаешь их JSON.",
        "",
        `Выполни: pwsh -File tools/tandem/gates.ps1 -Task ${t.id}`,
        "Ничего не правь. Верни ровно результат гейтов по схеме: ok, failed_step, tail.",
        "Если Unity.exe запущен — не убивай его, верни ok=false и failed_step=unity с объяснением в tail.",
      ].join("\n"),
      { model: plannerModel, provider: provider, label: `${t.id} gates`, schema: GATE_SCHEMA }
    );

    if (!full || !full.ok) {
      settled = { status: "unity-fail", attempts: attempts, exec: lastExec, review: lastReview, gates: full };
      stopped = "unity-fail";
      decide(t.id, "stop:unity-fail", { attempt: attempts });
      break;
    }

    settled = { status: "pass", attempts: attempts, exec: lastExec, review: lastReview, gates: full };
    decide(t.id, "pass", { attempt: attempts });
  }

  if (settled) {
    results.push(Object.assign({ id: t.id }, settled));
  }
  if (stopped) break;
}

// Инвариант 1: ни одна задача не может молча исчезнуть.
const seen = new Set(results.map(r => r.id));
for (const t of tasks) {
  if (!seen.has(t.id)) {
    results.push({ id: t.id, status: "missing" });
  }
}

// Инвариант 3: «успех» = каждая задача прошла и остановок не было.
const allPass = tasks.length > 0
  && results.length === tasks.length
  && results.every(r => r.status === "pass");
const runOk = stopped === null && allPass;

// Инвариант 2: финальная фаза — только при полном успехе.
if (runOk) {
  phase("ПК-билд");
  if (budgetOk()) {
    paidCall("gate", "build-windows");
    buildInfo = await agent(
      [
        "Собери Windows-плеер и проверь результат.",
        "Выполни: pwsh -File tools/unity/build-windows.ps1",
        "Проверь наличие unity-client/Builds/desktop/AlbionPrototype.exe.",
        "Ничего в коде не правь. Верни JSON по схеме.",
      ].join("\n"),
      { model: plannerModel, provider: provider, label: "build-windows", schema: BUILD_SCHEMA }
    );
  }
} else {
  buildInfo = { ok: false, skipped: true, error: "ПК-билд пропущен: не все задачи прошли (" + (stopped || "нет статуса pass") + ")" };
  log("ПК-билд пропущен: не все задачи прошли");
}

// Отчёт пишется всегда, чтобы провал был разобран, а не потерян.
if (budgetOk()) {
  paidCall("architect", "report");
  phase("Отчёт");
  reportText = await agent(
    [
      rolePrompt("architect"),
      "Ты архитектор конвейера «Тандем». Напиши отчёт по шаблону.",
      "",
      `Фича: ${cfg.feature || ""}`,
      `Итог прогона: done=${runOk}, stopped=${stopped || "null"}`,
      "Результаты задач:",
      JSON.stringify(results, null, 2),
      "Билд:",
      JSON.stringify(buildInfo, null, 2),
      "Журнал решений по задачам (инварианты T-008):",
      JSON.stringify(decisions, null, 2),
      "",
      "Платные вызовы этого прогона (журнал конвейера):",
      JSON.stringify(callLog, null, 2),
      "",
      // Имя файла отчёта: только [0-9a-zа-яё-], иначе NTFS/WSL падают на ':' и '('
      // (замер 22.11.2026: имя с двоеточием → EINVAL, отчёт записался с урезанным именем).
      "Напиши отчёт по шаблону docs/reports/_template.md в файл docs/reports/" +
        (cfg.feature || "feature")
          .toLowerCase()
          .replace(/[^0-9a-zа-яё]+/g, "-")
          .replace(/^-+|-+$/g, "")
          .slice(0, 60)
          .replace(/-+$/g, "") + ".md",
      "Дополнительно сохрани журнал решений как есть в docs/reports/logs/tandem-" +
        (cfg.feature || "feature").toLowerCase().replace(/[^0-9a-zа-яё]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40) +
        "-decisions.json (JSON-массив из блока «Журнал решений»).",
      "Обнови чекбоксы задач в docs/plan_for_gemma.md на [x] ТОЛЬКО для задач со статусом pass;",
      "если прогон неуспешен — не переключай ни один чекбокс и напиши в отчёте «фича не сделана».",
      "Допиши в .agent/spend.jsonl ПО СТРОКЕ НА КАЖДЫЙ вызов из журнала выше.",
      "Точных токенов у тебя нет — ставь \"estimated\": true и usd как оценку по .agent/config.json;",
      "не выдумывай точность, счётчик вызовов в конвейере важнее оценки денег.",
      "Верни короткое резюме отчёта текстом.",
    ].join("\n"),
    { model: plannerModel, provider: provider, label: "report" }
  );
}

return {
  done: runOk,
  stopped: stopped,
  reason: runOk
    ? "все задачи прошли"
    : "конвейер остановлен: " + (stopped || "не все задачи получили статус pass"),
  tasks: results,
  decisions: decisions,
  build: buildInfo,
  report: reportText,
  paidCalls: paidCalls,
  executorCalls: executorCalls,
};
