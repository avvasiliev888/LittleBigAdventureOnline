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
// Возврат: {done, stopped, reason, tasks:[...], report, build}

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

const results = [];
let stopped = null;
let reportText = "";
let buildInfo = null;

for (const t of tasks) {
  if (!budgetOk()) { stopped = "budget"; break; }
  phase(`Задача ${t.id}`);

  let attempts = 0;
  let gatesOk = false;
  let mustFix = [];
  let diagnosis = "";
  let lastExec = null;
  let lastReview = null;

  while (attempts < maxAttempts + 1) {
    attempts++;
    log(`${t.id}: попытка ${attempts}`);

    const exec = await agent(execPrompt(t, attempts, mustFix, diagnosis), {
      model: executorModel,
      provider: provider,
      label: `${t.id} executor #${attempts}`,
      schema: EXEC_SCHEMA,
    });
    executorCalls++;

    if (!exec) {
      log(`${t.id}: исполнитель не вернул результат — считаем провалом`);
      mustFix = ["исполнитель не вернул структурированный ответ"];
      continue;
    }
    lastExec = exec;
    if (exec.status === "blocked") {
      log(`${t.id}: исполнитель сообщил blocked: ${exec.summary}`);
      stopped = "blocked";
      break;
    }
    if (exec.gates_ok) { gatesOk = true; break; }

    log(`${t.id}: гейты упали на шаге ${exec.failed_step || "?"}`);
    mustFix = [`гейт «${exec.failed_step || "build"}» падает: ${(exec.log_tail || "").slice(-600)}`];
  }

  if (stopped === "blocked" || !gatesOk) {
    results.push({ id: t.id, status: stopped === "blocked" ? "blocked" : "gate-fail", attempts, exec: lastExec });
    if (stopped !== "blocked") stopped = "gate-fail";
    break;
  }

  // --- ревью ---
  if (!budgetOk()) { stopped = "budget"; break; }
  paidCall("reviewer", `${t.id} reviewer`);
  phase(`Ревью ${t.id}`);
  const review = await agent(
    [
      rolePrompt("reviewer"),
      "Ты ревьюер конвейера «Тандем». Ответ строго JSON по схеме.",
      "",
      "=== ЗАДАЧА ===",
      taskBlock(t),
      "",
      "=== ЧТО СДЕЛАЛ ИСПОЛНИТЕЛЬ ===",
      JSON.stringify(lastExec, null, 2),
      "",
      `Сам получи diff: git diff ${cfg.baseBranch || "main"} -- <файлы задачи> (или git diff --stat). Если база иная — спроси у себя: ветка сейчас agent/${t.id}.`,
      "Проверь по списку из своей роли. Ответ — только JSON по схеме.",
    ].join("\n"),
    { model: plannerModel, provider: provider, label: `${t.id} reviewer`, schema: REVIEW_SCHEMA }
  );
  lastReview = review;

  if (!review) {
    results.push({ id: t.id, status: "review-error", attempts, exec: lastExec });
    stopped = "review-error";
    break;
  }

  if (review.verdict === "FAIL") {
    log(`${t.id}: FAIL — ${review.must_fix.join("; ")}`);
    if (attempts >= maxAttempts + 1) {
      // --- отладчик ---
      if (!budgetOk()) { stopped = "budget"; break; }
      paidCall("debugger", `${t.id} debugger`);
      phase(`Отладка ${t.id}`);
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
      diagnosis = dbg || "";
      results.push({ id: t.id, status: "needs-human-debug", attempts, exec: lastExec, review: lastReview, diagnosis });
      stopped = "debug";
      break;
    }
    mustFix = review.must_fix;
    continue;
  }

  // --- PASS: полный гейт с Unity ---
  if (!budgetOk()) { stopped = "budget"; break; }
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
    results.push({ id: t.id, status: "unity-fail", attempts, exec: lastExec, review: lastReview, gates: full });
    stopped = "unity-fail";
    break;
  }

  results.push({ id: t.id, status: "pass", attempts, exec: lastExec, review: lastReview, gates: full });
  log(`${t.id}: PASS (попыток: ${attempts})`);
}

// --- финал: ПК-билд и отчёт ---
if (!stopped) {
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

  if (budgetOk()) {
    paidCall("architect", "report");
    phase("Отчёт");
    reportText = await agent(
      [
        rolePrompt("architect"),
        "Ты архитектор конвейера «Тандем». Напиши отчёт по шаблону.",
        "",
        `Фича: ${cfg.feature || ""}`,
        "Результаты задач:",
        JSON.stringify(results, null, 2),
        "Билд:",
        JSON.stringify(buildInfo, null, 2),
        "",
        "Платные вызовы этого прогона (журнал конвейера):",
        JSON.stringify(callLog, null, 2),
        "",
        "Напиши отчёт по шаблону docs/reports/_template.md в файл docs/reports/" +
          (cfg.feature || "feature").replace(/\s+/g, "-").toLowerCase() + ".md",
        "Обнови чекбоксы задач в docs/plan_for_gemma.md на [x] для задач со статусом pass.",
        "Допиши в .agent/spend.jsonl ПО СТРОКЕ НА КАЖДЫЙ вызов из журнала выше.",
        "Точных токенов у тебя нет — ставь \"estimated\": true и usd как оценку по .agent/config.json;",
        "не выдумывай точность, счётчик вызовов в конвейере важнее оценки денег.",
        "Верни короткое резюме отчёта текстом.",
      ].join("\n"),
      { model: plannerModel, provider: provider, label: "report" }
    );
  }
}

return {
  done: !stopped,
  stopped: stopped,
  reason: stopped ? "конвейер остановлен: " + stopped : "все задачи прошли",
  tasks: results,
  build: buildInfo,
  report: reportText,
  paidCalls: paidCalls,
  executorCalls: executorCalls,
};
