using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>Which hand weapon the local player has equipped. Presentation-only.</summary>
    public enum WeaponKind
    {
        Sword = 1,
        Axe = 2,
        Bow = 3,
        Staff = 4,
        Dagger = 5,
    }

    /// <summary>
    /// Legacy weapon switcher for the imported dReal warrior prefab. Professional character
    /// prefabs use <see cref="CharacterVisuals"/> instead, which mounts authored weapon prefabs.
    /// The server owns damage/range; this component only mirrors the snapshot's main-hand item.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisuals : MonoBehaviour
    {
        private const string SwordName = "Warrior_Sword";
        private const string AxeName = "Warrior_Axe";

        private Transform _sword;
        private Transform _axe;

        /// <summary>Currently visible weapon.</summary>
        public WeaponKind Equipped { get; private set; }

        /// <summary>Sword object, for legacy regression validation.</summary>
        public GameObject SwordObject => _sword != null ? _sword.gameObject : null;

        /// <summary>Axe object, for legacy regression validation.</summary>
        public GameObject AxeObject => _axe != null ? _axe.gameObject : null;

        /// <summary>Transform of the visible weapon; follows the hand socket.</summary>
        public Transform EquippedTransform => ActiveTransform();

        /// <summary>Adds the switcher to an instantiated imported Warrior visual and hides the axe.</summary>
        public static WeaponVisuals Attach(Transform root)
        {
            var sword = FindWeapon(root, SwordName);
            var axe = FindWeapon(root, AxeName);
            if (sword == null || axe == null)
            {
                throw new InvalidOperationException(
                    "Warrior prefab must contain " + SwordName + " and " + AxeName + ".");
            }

            var weapons = root.gameObject.AddComponent<WeaponVisuals>();
            weapons._sword = sword;
            weapons._axe = axe;
            weapons.Equip(WeaponKind.Sword);
            return weapons;
        }

        /// <summary>Shows exactly one of the vendor sword/axe models.</summary>
        public void Equip(WeaponKind kind)
        {
            if (!IsKnown(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown weapon.");
            }

            if (Equipped == kind && _sword != null && _axe != null
                && (_sword.gameObject.activeSelf || _axe.gameObject.activeSelf))
                return;
            Equipped = kind;
            if (_sword == null || _axe == null) return;
            // The vendor prefab only has sword/axe. Keep a visible weapon for bow/staff/dagger
            // until CharacterVisuals supplies the authored model; never leave the hand empty.
            var showAxe = kind == WeaponKind.Axe;
            _sword.gameObject.SetActive(!showAxe);
            _axe.gameObject.SetActive(showAxe);
        }

        /// <summary>
        /// Legacy fallback. The warrior prefab only includes sword/axe; bows and staves are
        /// served by <see cref="CharacterVisuals"/> on professional prefabs.
        /// </summary>
        public void EquipFromArchetype(string archetype)
        {
            Equip(string.Equals(archetype, "axe", StringComparison.OrdinalIgnoreCase)
                ? WeaponKind.Axe
                : WeaponKind.Sword);
        }

        private Transform ActiveTransform()
        {
            return Equipped == WeaponKind.Axe ? _axe : _sword;
        }

        private static bool IsKnown(WeaponKind kind)
        {
            return kind >= WeaponKind.Sword && kind <= WeaponKind.Dagger;
        }

        private static Transform FindWeapon(Transform root, string name)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
