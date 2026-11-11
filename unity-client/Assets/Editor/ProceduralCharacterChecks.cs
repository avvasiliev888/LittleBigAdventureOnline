using System;
using System.IO;
using System.Linq;
using Albion.Client;
using Client.Network;
using Simulation.Core;
using UnityEditor;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Albion.Editor
{
    /// <summary>Stage 8 regression checks for generated primitives, outlines and weapons.</summary>
    public static class ProceduralCharacterChecks
    {
        private static readonly ProceduralCharacterProfile[] Profiles =
        {
            ProceduralCharacterProfile.Hero,
            ProceduralCharacterProfile.BlueMob,
            ProceduralCharacterProfile.YellowMob,
            ProceduralCharacterProfile.RedMob,
        };

        /// <summary>Builds every archetype and checks its primitive budget and weapon mount.</summary>
        public static void ValidateAllProfiles()
        {
            var root = new GameObject("Procedural profile checks (transient)");
            try
            {
                for (var i = 0; i < Profiles.Length; i++)
                {
                    var height = Profiles[i] == ProceduralCharacterProfile.RedMob ? 2.55f
                        : Profiles[i] == ProceduralCharacterProfile.YellowMob ? 2.15f : 1.95f;
                    var visual = ProceduralHumanoidVisual.CreatePlayer(
                        root.transform, (uint)(i + 100), height, Profiles[i]);
                    Require(visual.Profile == Profiles[i], "Wrong generated profile: " + Profiles[i]);
                    Require(Mathf.Abs(visual.Height - height) < 0.001f, "Generated height mismatch.");
                    Require(visual.Weapons != null && visual.Weapons.IsProcedural,
                        "Generated character has no procedural weapon switcher.");

                    var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                    var outlines = renderers.Count(renderer => renderer.name == "Outline");
                    Require(renderers.Length >= 18, "Too few primitive parts: " + renderers.Length);
                    Require(outlines >= 12, "Too few inverted-hull outlines: " + outlines);
                    Require(visual.GetComponentInChildren<Animator>(true) == null,
                        "Procedural character must not depend on an Animator.");
                    Require(visual.GetComponentInChildren<SkinnedMeshRenderer>(true) == null,
                        "Procedural character must not contain a skinned mesh.");
                    Require(visual.GetComponentInChildren<CapsuleCollider>(true) == null,
                        "Procedural character must not use the old capsule.");
                    Require(visual.GetComponentsInChildren<Collider>(true).Length == 0,
                        "Procedural character must be collision-free.");

                    foreach (var renderer in renderers)
                    {
                        var shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader : null;
                        Require(shader != null, "Primitive renderer has no material.");
                        Require(shader.name == "Albion/Toon Lit" || shader.name == "Albion/Toon Outline",
                            "Unexpected generated shader: " + shader.name);
                        Require(!ShaderUtil.ShaderHasError(shader), "Generated shader has errors: " + shader.name);
                    }

                    var head = Find(visual.transform, "Head");
                    var torso = Find(visual.transform, "Torso");
                    var arm = Find(visual.transform, "Right Arm");
                    Require(head != null && head.GetComponent<MeshFilter>().sharedMesh == ProceduralMeshLibrary.Sphere,
                        "Head is not the low-poly sphere generator.");
                    Require(torso != null && torso.GetComponent<MeshFilter>().sharedMesh == ProceduralMeshLibrary.Box,
                        "Torso is not the box generator.");
                    Require(arm != null && arm.GetComponent<MeshFilter>().sharedMesh == ProceduralMeshLibrary.Cylinder,
                        "Arm is not the cylinder generator.");

                    var weapons = visual.Weapons;
                    weapons.Equip(WeaponKind.Bow);
                    Require(weapons.Equipped == WeaponKind.Bow && ActiveName(weapons).Contains("Bow"),
                        "Bow socket is missing.");
                    weapons.Equip(WeaponKind.Staff);
                    Require(weapons.Equipped == WeaponKind.Staff && ActiveName(weapons).Contains("Staff"),
                        "Staff socket is missing.");
                    weapons.Equip(WeaponKind.Sword);
                    Require(weapons.Equipped == WeaponKind.Sword && ActiveName(weapons).Contains("Sword"),
                        "Sword socket is missing.");
                    visual.PlayAttack();
                    Require(visual.IsAttackPlaying, "Generated attack did not start.");
                    visual.PlayHit();
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Checks the snapshot-driven UnityView path used by the smoke test.</summary>
        public static void Validate(CharacterVisualSettings settings)
        {
            Require(settings != null && settings.Procedural, "Character settings are not procedural.");
            var root = new GameObject("Procedural view checks (transient)");
            try
            {
                var snapshot = new SimulationSnapshot(7, new[]
                {
                    new EntitySnapshot(1, 1, new NVector3(2f, 0f, 1f), NVector3.Zero,
                        100, 100, 100, 100, 0, 0, false, weaponItemId: 14),
                    new EntitySnapshot(2, 1, new NVector3(-2f, 0f, 1f), NVector3.Zero,
                        100, 100, 100, 100, 0, 0, false, weaponItemId: 10),
                });
                var content = ClientContent.LoadFromDirectory(
                    Path.Combine(Application.dataPath, "StreamingAssets", "Config"));
                var view = new UnityView(root.transform, content) { PlayerEntityId = 1 };
                view.ApplySnapshot(snapshot);
                view.Render(0f);

                var player = view.Player;
                Require(player != null, "Procedural view did not spawn the player.");
                var generated = player.GetComponent<ProceduralHumanoidVisual>();
                Require(generated != null, "Player is not a generated humanoid.");
                Require(player.GetComponent<WarriorVisual>() == null, "Imported WarriorVisual still attached.");
                Require(player.GetComponent<FantasyVisuals>() == null, "Mob visual still attached to player.");
                Require(player.GetComponentInChildren<HealthBar>(true) != null, "Health bar missing.");
                Require(player.GetComponentInChildren<Animator>(true) == null, "Generated player has Animator.");

                var weapons = player.GetComponentInChildren<WeaponVisuals>(true);
                Require(weapons != null && weapons.IsProcedural, "Player weapon switcher missing.");
                Require(weapons.Equipped == WeaponKind.Bow,
                    "WeaponItemId 14 (hunter_bow) did not select the bow.");
                Require(ActiveName(weapons).Contains("Bow"), "Visible weapon is not the bow.");

                view.TriggerAttack();
                Require(generated.IsAttackPlaying, "TriggerAttack did not start the generated swing.");
                Require(view.IsPlayerAttackPlaying, "View did not report the generated attack.");

                var other = FindEntity(root.transform, 2);
                Require(other != null, "Remote generated entity missing.");
                var remoteWeapons = other.GetComponentInChildren<WeaponVisuals>(true);
                Require(remoteWeapons != null && remoteWeapons.Equipped == WeaponKind.Sword,
                    "WeaponItemId 10 (rusty_sword) did not select the sword.");

                view.PlayDamage(2);
                Require(FindEntity(root.transform, 2) != null, "Damage flash destroyed the entity.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
        }

        private static Transform FindEntity(Transform root, uint id)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "Entity " + id);
        }

        private static string ActiveName(WeaponVisuals weapons)
        {
            var active = weapons.EquippedTransform;
            return active != null ? active.name : string.Empty;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
