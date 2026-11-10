using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>Which hand weapon the local player has equipped. Presentation-only (M10.11).</summary>
    public enum WeaponKind
    {
        Sword = 1,
        Axe = 2,
    }

    /// <summary>
    /// Client-side weapon selection for the imported Warrior prefab. The vendor prefab contains
    /// both <c>Warrior_Sword</c> and <c>Warrior_Axe</c> under the right-hand socket; both were
    /// visible at once. This component shows exactly one, defaults to the sword, and lets the
    /// player switch with 1/2. The server is not involved: damage/range stay unchanged in M10.11.
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

        /// <summary>Sword object, for regression validation.</summary>
        public GameObject SwordObject => _sword != null ? _sword.gameObject : null;

        /// <summary>Axe object, for regression validation.</summary>
        public GameObject AxeObject => _axe != null ? _axe.gameObject : null;

        /// <summary>Transform of the visible weapon; follows the attack animation via the hand socket.</summary>
        public Transform EquippedTransform => Equipped == WeaponKind.Axe ? _axe : _sword;

        /// <summary>Adds the switcher to an instantiated Warrior visual and hides the axe.</summary>
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

        /// <summary>Shows exactly one weapon: sword for 1, axe for 2.</summary>
        public void Equip(WeaponKind kind)
        {
            if (_sword == null || _axe == null)
            {
                return;
            }

            if (kind != WeaponKind.Sword && kind != WeaponKind.Axe)
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown weapon.");
            }

            var changed = Equipped != kind;
            Equipped = kind;
            _sword.gameObject.SetActive(kind == WeaponKind.Sword);
            _axe.gameObject.SetActive(kind == WeaponKind.Axe);
            if (changed)
            {
                Debug.Log("WeaponVisuals: equipped " + kind
                    + " (sword active=" + _sword.gameObject.activeSelf
                    + ", axe active=" + _axe.gameObject.activeSelf + ")");
            }
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
