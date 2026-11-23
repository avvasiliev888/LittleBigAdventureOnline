using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Mob-key-to-imported-monster presentation map. Keeps mob prefabs out of gameplay code and
    /// falls back to the old primitive mobs when no authored prefab is configured.
    ///
    /// Keyed by the mob's own key from config/mobs.json, not by zone. A zone key allowed exactly
    /// one look per zone, so three zones meant three monster looks in the whole world no matter how
    /// many models existed — measured 21.11.2026, MobVisuals.asset held 3 entries for 3 zones.
    /// </summary>
    [CreateAssetMenu(menuName = "Albion/Mob Visual Settings")]
    public sealed class MobVisualSettings : ScriptableObject
    {
        public const string ResourcePath = "Characters/MobVisuals";

        [Serializable]
        public sealed class Entry
        {
            /// <summary>Mob key from config/mobs.json, e.g. "blue_scavenger".</summary>
            public string MobKey;
            public GameObject Prefab;
            [Min(0.1f)] public float Height = 1.8f;
            public float Yaw;
            public float GroundOffset;

            /// <summary>
            /// Authored weapon prefabs for this mob body. Left empty the mob stays unarmed; a mob
            /// holds the weapon it is given, not the one its rig happens to ship inside the FBX.
            /// </summary>
            public CharacterVisuals.WeaponBinding[] Weapons = Array.Empty<CharacterVisuals.WeaponBinding>();

            /// <summary>Weapon switched on right after the body is built, from the list above.</summary>
            public WeaponKind Equipped = WeaponKind.Sword;
            public bool AllowFlatColour;
        }

        public Entry[] Entries = Array.Empty<Entry>();

        /// <summary>Optional asset; absence means the primitive fallback stays active.</summary>
        public static MobVisualSettings Load()
        {
            return Resources.Load<MobVisualSettings>(ResourcePath);
        }

        /// <summary>
        /// Fails fast when an imported mob body has a white emission keyword or a missing albedo
        /// texture. This is the regression that caught the white Rogue/Warrior skeletons in the
        /// yellow and red zones: URP adds white emission on top of the albedo and washes them out.
        ///
        /// Also fails when two entries share a mob key: the first match wins silently, so a
        /// duplicated key would make one mob wear another's body with no error anywhere.
        /// </summary>
        public void ValidateMaterials()
        {
            if (Entries == null || Entries.Length == 0)
            {
                throw new InvalidOperationException("Mob visual settings have no mob entries.");
            }

            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in Entries)
            {
                if (entry == null || entry.Prefab == null)
                {
                    throw new InvalidOperationException("Mob visual entry has no prefab.");
                }

                if (string.IsNullOrWhiteSpace(entry.MobKey))
                {
                    throw new InvalidOperationException(
                        "Mob visual entry has no mob key: " + entry.Prefab.name);
                }

                if (!seen.Add(entry.MobKey))
                {
                    throw new InvalidOperationException(
                        "Duplicate mob key in mob visual settings: " + entry.MobKey);
                }

                var hasBody = false;
                var hasAlbedo = false;
                foreach (var renderer in entry.Prefab.GetComponentsInChildren<Renderer>(true))
                {
                    // A body is a skinned mesh, not a material whose name contains "skeleton".
                    // The old test only passed for KayKit, whose material happens to be called
                    // "skeleton"; the battle wizard's is "WizardBody" and was rejected with
                    // "no skeleton body material" — measured 21.11.2026, which made the check a
                    // gate that only let one art pack through.
                    if (!(renderer is SkinnedMeshRenderer))
                    {
                        continue;
                    }

                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            throw new InvalidOperationException(
                                "Mob prefab " + entry.Prefab.name + " has a missing material.");
                        }

                        hasBody = true;

                        if (HasAlbedo(material))
                        {
                            hasAlbedo = true;
                        }

                        // Only a near-white emission washes the model out. A coloured glow is a
                        // deliberate overlay — the KayKit skeletons ship M_Glow, red (1;0,18;0,08)
                        // and untextured, and an earlier version of this check rejected them for
                        // having no albedo. An overlay is not a body.
                        if (material.IsKeywordEnabled("_EMISSION")
                            && material.HasProperty("_EmissionColor"))
                        {
                            var emission = material.GetColor("_EmissionColor");
                            if (emission.r > 0.9f && emission.g > 0.9f && emission.b > 0.9f)
                            {
                                throw new InvalidOperationException(
                                    "Mob body material has white emission and will render white: "
                                    + material.name);
                            }
                        }
                        
                        if (!hasAlbedo && entry.AllowFlatColour)
                        {
                            var color = material.HasProperty("_BaseColor") 
                                ? material.GetColor("_BaseColor") 
                                : material.HasProperty("_Color") 
                                    ? material.GetColor("_Color") 
                                    : Color.white;
                            
                            var isWhite = color.r > 0.99f && color.g > 0.99f && color.b > 0.99f;
                            // URP Lit default: a body left at 0.8 means the pack colours were not
                            // transferred, and accepting it would silently ship a grey mannequin.
                            var isUrpDefault = Mathf.Abs(color.r - 0.8f) < 0.02f
                                && Mathf.Abs(color.g - 0.8f) < 0.02f
                                && Mathf.Abs(color.b - 0.8f) < 0.02f;

                            if (!isWhite && !isUrpDefault)
                            {
                                hasAlbedo = true;
                            }
                        }
                    }
                }

                if (!hasBody)
                {
                    throw new InvalidOperationException(
                        "Mob prefab has no skinned body mesh: " + entry.Prefab.name);
                }

                // One textured material is enough: a body plus an untextured glow overlay is a
                // normal setup. Zero textured materials is the real defect — it means the model
                // imported with no albedo and will render flat grey.
                if (!hasAlbedo)
                {
                    throw new InvalidOperationException(
                        "Mob prefab has no textured body material: " + entry.Prefab.name + 
                        (entry.AllowFlatColour ? " (check AllowFlatColour settings)" : ""));
                }
            }
        }

        /// <summary>Creates a presentation root for a mob key, or null if the mob is not mapped.</summary>
        public Transform Create(Transform parent, uint entityId, string mobKey)
        {
            var entry = Find(mobKey);
            if (entry == null || entry.Prefab == null) return null;
            var settings = CreateInstance<CharacterVisualSettings>();
            try
            {
                settings.Prefab = entry.Prefab;
                settings.Height = entry.Height;
                settings.Yaw = entry.Yaw;
                settings.GroundOffset = entry.GroundOffset;
                var weapons = entry.Weapons ?? Array.Empty<CharacterVisuals.WeaponBinding>();
                settings.Weapons = weapons;
                var root = settings.Create(parent, entityId);

                // The weapon is switched on here, after the body exists (Equip needs the sockets
                // resolved by Initialize) and before the settings object is destroyed in finally.
                // An entry that authored no prefabs stays unarmed instead of warning on every
                // spawn: Equip logs "no authored binding" for a kind it cannot place in a hand.
                var character = root != null ? root.GetComponentInChildren<CharacterVisuals>(true) : null;
                if (character != null && weapons.Length > 0)
                {
                    character.Equip(entry.Equipped);
                }

                return root;
            }
            finally
            {
                Destroy(settings);
            }
        }

        /// <summary>Prefab name and requested height for a mob key, for diagnostics.</summary>
        public string Describe(string mobKey, out float height)
        {
            height = 0f;
            var entry = Find(mobKey);
            if (entry == null || entry.Prefab == null) return "<none>";
            height = entry.Height;
            return entry.Prefab.name;
        }

        private Entry Find(string mobKey)
        {
            if (Entries == null || string.IsNullOrEmpty(mobKey)) return null;
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i] != null
                    && string.Equals(Entries[i].MobKey, mobKey, StringComparison.OrdinalIgnoreCase))
                {
                    return Entries[i];
                }
            }
            return null;
        }
    private static bool HasAlbedo(Material material)
    {
        if (material == null) return false;
        string[] props = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" };
        foreach (var prop in props)
        {
            if (material.HasProperty(prop) && material.GetTexture(prop) != null) return true;
        }
        return false;
    }
}
}
