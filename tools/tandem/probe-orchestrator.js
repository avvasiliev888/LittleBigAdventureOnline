// Временный стенд: прогоняет тело orchestrator.workflow.js с подставными agent().
// Нужен, чтобы увидеть, что скрипт возвращает в сценарии «ревью FAIL»,
// не тратя платных вызовов. Не коммитить.
const fs = require("fs");

const src = fs.readFileSync("tools/tandem/orchestrator.workflow.js", "utf8");

function run(scenario, extra) {
  const calls = [];
  const agent = async (prompt, opts) => {
    const label = (opts && opts.label) || "?";
    calls.push(label);
    if (label.indexOf("executor") >= 0) {
      return { status: "done", branch: "agent/T", summary: "ok", changed_files: ["x"], gates_ok: true };
    }
    if (label.indexOf("reviewer") >= 0) {
      return scenario === "review-fail"
        ? { verdict: "FAIL", must_fix: ["не тронул файл"], risk: "high", notes: "x" }
        : scenario === "review-null"
          ? null
          : { verdict: "PASS", must_fix: [], risk: "low", notes: "ok" };
    }
    if (label.indexOf("gates") >= 0) return { ok: scenario !== "unity-fail", failed_step: "unity", tail: "CS1513" };
    if (label === "build-windows") return { ok: true, exe_path: "x.exe", error: "" };
    return "report";
  };
  const args = Object.assign(
    {
      feature: "probe",
      tasks: [{ id: "T-PROBE", title: "проба", body: "тело" }],
      promptFiles: {},
      capCalls: 60,
      spend: { calls: 0 },
      maxAttempts: 3,
    },
    extra || {}
  );
  const body = "return (async () => {\n" + src + "\n})()";
  const fn = new Function("args", "agent", "phase", "log", body);
  return fn(args, agent, () => {}, () => {}).then((result) => {
    console.log("=== " + scenario + " ===");
    console.log("agents:", calls.join(", ") || "(none)");
    console.log("done:", result && result.done, "| stopped:", result && result.stopped);
    console.log("tasks:", JSON.stringify(result && result.tasks).slice(0, 300));
    console.log("build:", JSON.stringify(result && result.build));
    console.log("");
  });
}

(async () => {
  await run("all-pass");
  await run("review-fail");
  await run("review-null");
  await run("unity-fail");
  await run("pass-but-no-build-budget", { capCalls: 2 });
  await run("empty-tasks", { tasks: [] });
})();
