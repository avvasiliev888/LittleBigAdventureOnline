using System;
using System.Linq;
using Albion.Client;
using Client.Network;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>Live Play-mode checks for the Stage 8 primitives, weapons and short VFX.</summary>
    public static class Stage8VisualChecks
    {
        public static void Verify(UnityView view, ClientContent content)
        {
            if (view == null) throw new InvalidOperationException("Stage 8: UnityView отсутствует.");
            var generated = UnityEngine.Object.FindObjectsByType<ProceduralHumanoidVisual>(
                UnityEngine.FindObjectsSortMode.None);
            if (generated.Length < 7)
            {
                throw new InvalidOperationException("Stage 8: процедурных персонажей меньше 7: " + generated.Length);
            }

            var renderers = 0;
            var outlines = 0;
            foreach (var visual in generated)
            {
                if (visual == null) continue;
                if (visual.GetComponentInChildren<Animator>(true) != null)
                {
                    throw new InvalidOperationException("Stage 8: у процедурной модели остался Animator.");
                }
                if (visual.GetComponentInChildren<CapsuleCollider>(true) != null)
                {
                    throw new InvalidOperationException("Stage 8: процедурная модель использует капсулу.");
                }
                if (visual.Weapons == null || !visual.Weapons.IsProcedural)
                {
                    throw new InvalidOperationException("Stage 8: у модели нет процедурного оружия.");
                }

                var parts = visual.GetComponentsInChildren<MeshRenderer>(true);
                renderers += parts.Length;
                outlines += parts.Count(renderer => renderer.name == "Outline");
                var head = visual.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => child.name == "Head");
                if (head == null || head.GetComponent<MeshFilter>().sharedMesh != ProceduralMeshLibrary.Sphere)
                {
                    throw new InvalidOperationException("Stage 8: голова не является сферой генератора.");
                }
            }

            if (outlines < generated.Length * 12)
            {
                throw new InvalidOperationException("Stage 8: контуров меньше ожидаемого: " + outlines);
            }

            view.TriggerAttack();
            if (view.Player == null || !view.IsPlayerAttackPlaying)
            {
                throw new InvalidOperationException("Stage 8: VFX атаки не запустился.");
            }

            view.TriggerGather(view.Player.position);
            view.PlayDamage(view.PlayerEntityId);
            if (UnityEngine.Object.FindObjectsByType<ParticleSystem>(
                    UnityEngine.FindObjectsSortMode.None).Length == 0)
            {
                throw new InvalidOperationException("Stage 8: частицы сбора не созданы.");
            }

            Debug.Log("[stage8] generated=" + generated.Length
                + " renderers=" + renderers
                + " outlines=" + outlines
                + " vfx=attack+gather+damage");
            if (content == null)
            {
                Debug.LogWarning("[stage8] content отсутствует, проверка оружия по предметам пропущена.");
            }
        }
    }
}
