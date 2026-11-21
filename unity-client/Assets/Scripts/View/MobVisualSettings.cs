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

                        if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
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
                        "Mob prefab has no textured body material: " + entry.Prefab.name);
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
                return settings.Create(parent, entityId);
            }
            finally
            {
                Destroy(settings);
            }
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
    }
}
