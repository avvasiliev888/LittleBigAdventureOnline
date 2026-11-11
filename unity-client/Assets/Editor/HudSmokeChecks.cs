#if UNITY_EDITOR
using System;
using Albion.Client;
using Simulation.Core;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace Albion.Editor
{
    /// <summary>Deterministic Editor regressions for own vitals, mobile geometry and snapshot-to-bar wiring.</summary>
    public static class HudSmokeChecks
    {
        /// <summary>
        /// Call once from the existing Play-mode smoke gate, after content/visual resources load.
        /// Throws on failure; no server, keyboard input, screenshots or physical Android device required.
        /// </summary>
        public static void Verify()
        {
            CheckRatiosAndGeometry();
            CheckOwnSnapshotSelection();
            CheckViewSnapshotUpdates();
            Debug.Log("[agent-smoke] HUD own snapshot/HP/mana/death/regen/mobile geometry checks PASS");
        }

        private static void CheckRatiosAndGeometry()
        {
            Equal(HealthBar.Fraction(75, 100), 0.75f, "HP fraction");
            Equal(HealthBar.Fraction(90, 100), 0.9f, "mana fraction");
            Equal(HealthBar.Fraction(-1, 100), 0f, "negative clamps");
            Equal(HealthBar.Fraction(150, 100), 1f, "overflow clamps");
            Equal(HealthBar.Fraction(100, 0), 0f, "zero maximum");
            Equal(HealthBar.Fraction(100, -1), 0f, "negative maximum");
            var full = HealthBar.FillRect(new Rect(20f, 30f, 104f, 20f), 1f);
            var half = HealthBar.FillRect(new Rect(20f, 30f, 104f, 20f), 0.5f);
            Equal(full.width, 100f, "full fill width");
            Equal(half.width, 50f, "half fill width");
            Equal(half.x, full.x, "fill must stay left anchored");
            Equal(HealthBar.FillRect(new Rect(0f, 0f, 104f, 20f), 0f).width, 0f, "empty fill");
            foreach (var height in new[] { 480f, 720f, 1080f, 1440f })
            {
                var size = HealthBar.MeterSize(height);
                Require(size.x >= 88f && size.y >= 10f, "mobile overhead meter too small");
                Require(HealthBar.FillRect(new Rect(0f, 0f, size.x, size.y), 0.1f).width >= 8f,
                    "ten-point mana change must cover at least eight pixels");
            }
            foreach (var size in new[] { new Vector2(1280f, 720f), new Vector2(1920f, 1080f), new Vector2(2400f, 1080f) })
            {
                var rect = ClientHud.VitalsPanelRect(size.x, size.y, true);
                var controlScale = Mathf.Clamp(size.y / 720f, 1f, 3f);
                var controlsLeft = (size.x - (112f * 3f + 8f * 2f) * controlScale) * 0.5f;
                Require(rect.xMax < controlsLeft, "vitals overlap mobile top-centre buttons");
                Require(rect.yMax < size.y * 0.65f, "vitals overlap mobile joystick zone");
            }
        }

        private static void CheckOwnSnapshotSelection()
        {
            var snapshot = new SimulationSnapshot(100, new[] { Entity(2, 100, 100), Entity(7, 75, 90) });
            Require(ClientHud.TryGetOwnVitals(snapshot, 7, out var own), "own entity absent");
            Require(own.Id == 7 && own.Hp == 75 && own.Resource == 90, "selected a mob or stale vitals");
            Require(!ClientHud.TryGetOwnVitals(snapshot, 999, out _), "missing entity must not borrow mob vitals");
            Require(!ClientHud.TryGetOwnVitals(snapshot, 0, out _), "unconnected entity has no vitals");
            Require(!ClientHud.TryGetOwnVitals(null, 7, out _), "missing snapshot must not report full bars");
            var next = new SimulationSnapshot(104, new[] { Entity(7, 50, 95) });
            Require(ClientHud.TryGetOwnVitals(next, 7, out own) && own.Hp == 50 && own.Resource == 95,
                "screen HUD must use newest authoritative HP and regenerating mana");
        }

        private static void CheckViewSnapshotUpdates()
        {
            var root = new GameObject("HUD regression fixture");
            try
            {
                var view = new UnityView(root.transform) { PlayerEntityId = 7 };
                view.ApplySnapshot(new SimulationSnapshot(100, new[] { Entity(7, 100, 100) }));
                view.Render(0.1f);
                var bar = root.GetComponentInChildren<HealthBar>();
                Require(bar != null, "UnityView did not attach HealthBar to character");
                Equal(bar.HpFraction, 1f, "initial HP");
                Equal(bar.ResourceFraction, 1f, "initial mana");
                ApplyPair(view, 110, 75, 90);
                Equal(bar.HpFraction, 0.75f, "snapshot damage reaches overhead HP");
                Equal(bar.ResourceFraction, 0.9f, "snapshot cost reaches overhead mana");
                ApplyPair(view, 120, 75, 95);
                Equal(bar.ResourceFraction, 0.95f, "snapshot regeneration reaches overhead mana");
                ApplyPair(view, 130, 0, 0);
                Equal(bar.HpFraction, 0f, "dead HP remains empty");
                Equal(bar.ResourceFraction, 0f, "zero mana remains empty");
                ApplyPair(view, 140, 100, 100);
                Equal(bar.HpFraction, 1f, "respawn HP reset");
                Equal(bar.ResourceFraction, 1f, "respawn mana reset");
                Require(root.GetComponentsInChildren<HealthBar>(true).Length == 1, "snapshot update duplicated bars");
            }
            finally
            {
                // This root belongs only to the test; never destroy any live client/editor/server object.
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ApplyPair(UnityView view, uint tick, int hp, int mana)
        {
            // Render buffer intentionally delays discrete fields. Put the changed value in both
            // snapshots of the interpolation pair rather than demanding zero-delay remote state.
            view.ApplySnapshot(new SimulationSnapshot(tick, new[] { Entity(7, hp, mana) }));
            view.ApplySnapshot(new SimulationSnapshot(tick + 4, new[] { Entity(7, hp, mana) }));
            view.Render(1f);
        }

        private static EntitySnapshot Entity(uint id, int hp, int mana)
        {
            return new EntitySnapshot(id, 1, NumericsVector3.Zero, NumericsVector3.Zero,
                hp, 100, mana, 100, 0, hp == 0 ? 200u : 0u, hp == 0);
        }

        private static void Equal(float actual, float expected, string message)
        {
            Require(Mathf.Abs(actual - expected) < 0.0001f, message + ": " + actual + " != " + expected);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("HUD regression: " + message);
        }
    }
}
#endif
