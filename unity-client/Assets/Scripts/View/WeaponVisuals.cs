using System;
using System.Collections.Generic;
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
    /// Client-side weapon selection. Supports the imported Warrior prefab (sword/axe sockets)
    /// and the generated procedural humanoid (sword, axe, bow, staff and dagger). The server
    /// owns damage/range; this component only mirrors the snapshot's main-hand item.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisuals : MonoBehaviour
    {
        private const string SwordName = "Warrior_Sword";
        private const string AxeName = "Warrior_Axe";

        private Transform _sword;
        private Transform _axe;
        private readonly Dictionary<WeaponKind, Transform> _generated = new Dictionary<WeaponKind, Transform>();
        private Transform _generatedSocket;

        /// <summary>Currently visible weapon.</summary>
        public WeaponKind Equipped { get; private set; }

        /// <summary>True when this instance was created for the procedural primitives.</summary>
        public bool IsProcedural => _generatedSocket != null;

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

        /// <summary>Builds all generated weapons under a hand socket and shows the sword.</summary>
        public static WeaponVisuals AttachProcedural(GameObject root, Transform rightHandSocket)
        {
            var weapons = root.AddComponent<WeaponVisuals>();
            weapons._generatedSocket = rightHandSocket;
            foreach (WeaponKind kind in Enum.GetValues(typeof(WeaponKind)))
            {
                weapons._generated[kind] = ProceduralWeaponBuilder.Create(kind, rightHandSocket);
            }
            weapons.Equip(WeaponKind.Sword);
            return weapons;
        }

        /// <summary>Shows exactly one weapon.</summary>
        public void Equip(WeaponKind kind)
        {
            if (!IsKnown(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown weapon.");
            }

            var changed = Equipped != kind;
            Equipped = kind;
            if (IsProcedural)
            {
                foreach (var pair in _generated)
                {
                    if (pair.Value != null)
                    {
                        pair.Value.gameObject.SetActive(pair.Key == kind);
                    }
                }
            }
            else
            {
                if (_sword == null || _axe == null)
                {
                    return;
                }
                _sword.gameObject.SetActive(kind == WeaponKind.Sword);
                _axe.gameObject.SetActive(kind == WeaponKind.Axe);
            }

            if (changed)
            {
                Debug.Log("WeaponVisuals: equipped " + kind);
            }
        }

        /// <summary>Chooses the visible weapon from the server item archetype.</summary>
        public void EquipFromArchetype(string archetype)
        {
            if (string.Equals(archetype, "archer", StringComparison.OrdinalIgnoreCase))
            {
                Equip(WeaponKind.Bow);
            }
            else if (string.Equals(archetype, "mage", StringComparison.OrdinalIgnoreCase))
            {
                Equip(WeaponKind.Staff);
            }
            else
            {
                Equip(WeaponKind.Sword);
            }
        }

        private Transform ActiveTransform()
        {
            if (IsProcedural)
            {
                return _generated.TryGetValue(Equipped, out var generated) ? generated : null;
            }
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
