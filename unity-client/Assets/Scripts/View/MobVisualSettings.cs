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
