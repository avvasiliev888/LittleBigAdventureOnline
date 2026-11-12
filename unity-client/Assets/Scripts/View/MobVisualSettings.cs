using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Zone-to-imported-monster presentation map. Keeps mob prefabs out of gameplay code and
    /// falls back to the old primitive mobs when no authored prefab is configured.
    /// </summary>
    [CreateAssetMenu(menuName = "Albion/Mob Visual Settings")]
    public sealed class MobVisualSettings : ScriptableObject
    {
        public const string ResourcePath = "Characters/MobVisuals";

        [Serializable]
        public sealed class Entry
        {
            public string ZoneKey;
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
        /// </summary>
        public void ValidateMaterials()
        {
            if (Entries == null || Entries.Length == 0)
            {
                throw new InvalidOperationException("Mob visual settings have no zone entries.");
            }

            foreach (var entry in Entries)
            {
                if (entry == null || entry.Prefab == null)
                {
                    throw new InvalidOperationException("Mob visual entry has no prefab.");
                }

                var hasBody = false;
                foreach (var renderer in entry.Prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            throw new InvalidOperationException(
                                "Mob prefab " + entry.Prefab.name + " has a missing material.");
                        }

                        if (material.name.IndexOf("skeleton", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        hasBody = true;
                        if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") == null)
                        {
                            throw new InvalidOperationException(
                                "Mob body material has no albedo texture: " + material.name);
                        }

                        if (material.IsKeywordEnabled("_EMISSION")
                            && material.HasProperty("_EmissionColor")
                            && material.GetColor("_EmissionColor").maxColorComponent > 0.05f)
                        {
                            throw new InvalidOperationException(
                                "Mob body material has white emission and will render white: " + material.name);
                        }
                    }
                }

                if (!hasBody)
                {
                    throw new InvalidOperationException(
                        "Mob prefab has no skeleton body material: " + entry.Prefab.name);
                }
            }
        }

        /// <summary>Creates a presentation root for a zone, or null if the zone is not mapped.</summary>
        public Transform Create(Transform parent, uint entityId, string zoneKey)
        {
            var entry = Find(zoneKey);
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

        private Entry Find(string zoneKey)
        {
            if (Entries == null || string.IsNullOrEmpty(zoneKey)) return null;
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i] != null
                    && string.Equals(Entries[i].ZoneKey, zoneKey, StringComparison.OrdinalIgnoreCase))
                {
                    return Entries[i];
                }
            }
            return null;
        }
    }
}
