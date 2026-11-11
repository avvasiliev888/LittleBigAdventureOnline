using System;
using System.Collections.Generic;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>Visual archetype for the generated primitive characters.</summary>
    public enum ProceduralCharacterProfile
    {
        Hero = 0,
        BlueMob = 1,
        YellowMob = 2,
        RedMob = 3,
    }

    /// <summary>
    /// Presentation-only humanoid assembled from low-poly primitives: sphere head, box torso,
    /// cylinder arms/legs and an inverted-hull outline. No collider, rigidbody or network state
    /// lives here; the network root is moved only by <see cref="UnityView"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralHumanoidVisual : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private readonly List<Renderer> _bodyRenderers = new List<Renderer>();
        private readonly List<Color> _baseColors = new List<Color>();
        private readonly List<Quaternion> _restArm = new List<Quaternion>();
        private MaterialPropertyBlock _tint;

        private Transform _facing;
        private Transform _rig;
        private Transform _head;
        private Transform _leftArm;
        private Transform _rightArm;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private float _walkPhase;
        private float _speed;
        private bool _dead;
        private float _attackStarted = -10f;
        private float _attackUntil = -10f;
        private float _flashUntil = -10f;
        private float _height = 1.9f;
        private bool _built;

        /// <summary>Selected visual archetype.</summary>
        public ProceduralCharacterProfile Profile { get; private set; }

        /// <summary>Root that turns toward movement without touching the network root.</summary>
        public Transform Facing => _facing;

        /// <summary>Weapon sockets and visible weapon models, when present.</summary>
        public WeaponVisuals Weapons { get; private set; }

        /// <summary>Height in metres used for the overhead health bar.</summary>
        public float Height => _height;

        /// <summary>True until the current attack swing has finished.</summary>
        public bool IsAttackPlaying => Time.time < _attackUntil;

        /// <summary>Creates a generated player-style humanoid.</summary>
        public static ProceduralHumanoidVisual CreatePlayer(
            Transform parent, uint entityId, float height, ProceduralCharacterProfile profile)
        {
            var root = new GameObject("Entity " + entityId);
            root.transform.SetParent(parent, false);
            root.SetActive(false);
            var visual = root.AddComponent<ProceduralHumanoidVisual>();
            visual.Initialize(parent, entityId, profile, height);
            root.SetActive(true);
            return visual;
        }

        /// <summary>Creates a generated mob humanoid for the zone colour.</summary>
        public static ProceduralHumanoidVisual CreateMob(
            Transform parent, uint entityId, string zone)
        {
            var profile = zone == "red" ? ProceduralCharacterProfile.RedMob
                : zone == "yellow" ? ProceduralCharacterProfile.YellowMob
                : ProceduralCharacterProfile.BlueMob;
            var height = zone == "red" ? 2.55f : zone == "yellow" ? 2.15f : 1.95f;
            return CreatePlayer(parent, entityId, height, profile);
        }

        /// <summary>Builds the visual hierarchy; safe to call once.</summary>
        public void Initialize(Transform parent, uint entityId, ProceduralCharacterProfile profile, float height)
        {
            if (_built) return;
            _built = true;
            Profile = profile;
            _height = Mathf.Max(0.8f, height);
            var scale = _height / 1.9f;

            _facing = new GameObject("Visual Facing").transform;
            _facing.SetParent(transform, false);
            _rig = new GameObject("Procedural Rig").transform;
            _rig.SetParent(_facing, false);
            _rig.localScale = Vector3.one * scale;

            BuildBody(profile);
            BuildWeapons(profile);
            ResetPose();
        }

        private void BuildBody(ProceduralCharacterProfile profile)
        {
            var palette = Palette.For(profile);
            var big = profile == ProceduralCharacterProfile.RedMob;
            var hooded = profile == ProceduralCharacterProfile.BlueMob
                || profile == ProceduralCharacterProfile.YellowMob;
            var torsoWidth = big ? 0.72f : profile == ProceduralCharacterProfile.Hero ? 0.58f : 0.54f;
            var torsoHeight = big ? 0.64f : 0.54f;

            Part("Hips", _rig, ProceduralMeshLibrary.Box, palette.Leather,
                new Vector3(0f, 0.88f, 0f), new Vector3(torsoWidth * 0.78f, 0.2f, 0.3f));
            Part("Torso", _rig, ProceduralMeshLibrary.Box, palette.Cloth,
                new Vector3(0f, 1.22f, 0f), new Vector3(torsoWidth, torsoHeight, 0.34f));
            Part("Chest Guard", _rig, ProceduralMeshLibrary.Box, palette.Metal,
                new Vector3(0f, 1.3f, 0.18f), new Vector3(torsoWidth * 0.72f, torsoHeight * 0.6f, 0.08f));
            Part("Belt", _rig, ProceduralMeshLibrary.Box, palette.Accent,
                new Vector3(0f, 0.96f, -0.02f), new Vector3(torsoWidth * 0.82f, 0.08f, 0.36f));

            _head = Part("Head", _rig, ProceduralMeshLibrary.Sphere, palette.Skin,
                new Vector3(0f, 1.68f, 0f), new Vector3(0.34f, 0.38f, 0.34f));
            Part("Hair", _rig, ProceduralMeshLibrary.Sphere, palette.Hair,
                new Vector3(0f, 1.75f, 0.03f), new Vector3(0.35f, 0.28f, 0.35f), outline: false);
            Part("Face Mark", _rig, ProceduralMeshLibrary.Box, palette.Accent,
                new Vector3(0f, 1.68f, 0.19f), new Vector3(0.16f, 0.04f, 0.02f), outline: false);

            if (hooded)
            {
                Part("Hood", _rig, ProceduralMeshLibrary.Cone, palette.Cloth,
                    new Vector3(0f, 1.89f, -0.02f), new Vector3(0.42f, 0.48f, 0.42f));
                Part("Hood Brim", _rig, ProceduralMeshLibrary.Cylinder, palette.Dark,
                    new Vector3(0f, 1.79f, -0.01f), new Vector3(0.44f, 0.08f, 0.44f), outline: false);
            }
            else
            {
                Part("Helmet", _rig, ProceduralMeshLibrary.Sphere, palette.Metal,
                    new Vector3(0f, 1.79f, 0f), new Vector3(0.38f, 0.26f, 0.38f));
                Part("Helmet Crest", _rig, ProceduralMeshLibrary.Cone, palette.Accent,
                    new Vector3(0f, 1.98f, 0f), new Vector3(0.16f, 0.32f, 0.16f));
            }

            if (big)
            {
                Part("Horn L", _rig, ProceduralMeshLibrary.Cone, palette.Bone,
                    new Vector3(-0.25f, 1.92f, 0f), new Vector3(0.16f, 0.38f, 0.16f));
                Part("Horn R", _rig, ProceduralMeshLibrary.Cone, palette.Bone,
                    new Vector3(0.25f, 1.92f, 0f), new Vector3(0.16f, 0.38f, 0.16f));
                Part("Shoulder L", _rig, ProceduralMeshLibrary.Sphere, palette.Metal,
                    new Vector3(-0.46f, 1.48f, 0f), new Vector3(0.3f, 0.22f, 0.28f));
                Part("Shoulder R", _rig, ProceduralMeshLibrary.Sphere, palette.Metal,
                    new Vector3(0.46f, 1.48f, 0f), new Vector3(0.3f, 0.22f, 0.28f));
            }

            _leftArm = BuildArm("Left", -0.38f, palette.Cloth, palette.Skin);
            _rightArm = BuildArm("Right", 0.38f, palette.Cloth, palette.Skin);
            _leftLeg = BuildLeg("Left", -0.15f, palette.Leather, palette.Metal);
            _rightLeg = BuildLeg("Right", 0.15f, palette.Leather, palette.Metal);
            _restArm.Add(_leftArm.localRotation);
            _restArm.Add(_rightArm.localRotation);
        }

        private Transform BuildArm(string side, float x, Color cloth, Color skin)
        {
            var pivot = new GameObject(side + " Arm Pivot").transform;
            pivot.SetParent(_rig, false);
            pivot.localPosition = new Vector3(x, 1.45f, 0f);
            Part(side + " Arm", pivot, ProceduralMeshLibrary.Cylinder, cloth,
                new Vector3(0f, -0.27f, 0f), new Vector3(0.16f, 0.5f, 0.16f));
            Part(side + " Hand", pivot, ProceduralMeshLibrary.Sphere, skin,
                new Vector3(0f, -0.55f, 0.02f), new Vector3(0.17f, 0.16f, 0.17f), outline: false);
            if (side == "Right")
            {
                var socket = new GameObject("RightHandSocket").transform;
                socket.SetParent(pivot, false);
                socket.localPosition = new Vector3(0f, -0.58f, 0.04f);
            }
            return pivot;
        }

        private Transform BuildLeg(string side, float x, Color cloth, Color metal)
        {
            var pivot = new GameObject(side + " Leg Pivot").transform;
            pivot.SetParent(_rig, false);
            pivot.localPosition = new Vector3(x, 0.82f, 0f);
            Part(side + " Leg", pivot, ProceduralMeshLibrary.Cylinder, cloth,
                new Vector3(0f, -0.34f, 0f), new Vector3(0.18f, 0.7f, 0.18f));
            Part(side + " Boot", pivot, ProceduralMeshLibrary.Box, metal,
                new Vector3(0f, -0.7f, 0.08f), new Vector3(0.22f, 0.13f, 0.38f));
            return pivot;
        }

        private void BuildWeapons(ProceduralCharacterProfile profile)
        {
            var socket = FindChild(transform, "RightHandSocket");
            if (socket == null)
            {
                return;
            }

            Weapons = WeaponVisuals.AttachProcedural(gameObject, socket);
            switch (profile)
            {
                case ProceduralCharacterProfile.BlueMob:
                    Weapons.Equip(WeaponKind.Dagger);
                    break;
                case ProceduralCharacterProfile.YellowMob:
                    Weapons.Equip(WeaponKind.Axe);
                    break;
                case ProceduralCharacterProfile.RedMob:
                    Weapons.Equip(WeaponKind.Axe);
                    break;
                default:
                    Weapons.Equip(WeaponKind.Sword);
                    break;
            }
        }

        private Transform Part(
            string name,
            Transform parent,
            Mesh mesh,
            Color color,
            Vector3 position,
            Vector3 scale,
            bool outline = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralMaterials.Lit(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            _bodyRenderers.Add(renderer);
            _baseColors.Add(color);

            if (outline)
            {
                var hull = new GameObject("Outline");
                hull.transform.SetParent(go.transform, false);
                hull.AddComponent<MeshFilter>().sharedMesh = mesh;
                var hullRenderer = hull.AddComponent<MeshRenderer>();
                hullRenderer.sharedMaterial = ProceduralMaterials.Outline;
                hullRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hullRenderer.receiveShadows = false;
            }

            return go.transform;
        }

        /// <summary>Sets presentation locomotion without moving the network root.</summary>
        public void SetMotion(Vector3 velocity, bool dead, bool reset = false)
        {
            SetMotion(velocity, dead, reset, Time.deltaTime);
        }

        /// <summary>Sets presentation locomotion from the view's snapshot delta.</summary>
        public void SetMotion(Vector3 velocity, bool dead, bool reset, float deltaTime)
        {
            if (!_built)
            {
                return;
            }

            velocity.y = 0f;
            var speed = velocity.magnitude;
            if (!float.IsFinite(speed) || speed <= 0.08f)
            {
                speed = 0f;
            }

            _speed = dead || reset ? 0f : speed;
            if (_speed > 0f && _facing != null)
            {
                var direction = velocity / speed;
                _facing.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            if (dead && !_dead)
            {
                _dead = true;
                _attackUntil = -10f;
            }
            else if (!dead && _dead)
            {
                _dead = false;
                ResetPose();
            }
            else if (reset && !dead)
            {
                ResetPose();
            }

            if (_dead)
            {
                _rig.localRotation = Quaternion.Euler(82f, 0f, 0f);
                _rig.localPosition = new Vector3(0f, 0.06f, 0f);
            }
        }

        private void Update()
        {
            if (!_built || _dead)
            {
                UpdateTint();
                return;
            }

            var delta = Mathf.Min(Time.deltaTime, 0.1f);
            if (_speed > 0.08f)
            {
                _walkPhase += delta * (5.5f + (_speed * 0.6f));
            }
            else
            {
                _walkPhase += delta * 1.3f;
            }

            var swing = Mathf.Sin(_walkPhase) * Mathf.Min(30f, _speed * 5f);
            _leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
            _rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            _leftArm.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 0f);
            _rightArm.localRotation = Quaternion.Euler(swing * 0.8f, 0f, 0f);

            if (Time.time < _attackUntil)
            {
                var progress = Mathf.InverseLerp(_attackStarted, _attackUntil, Time.time);
                var swingAngle = Mathf.Sin(progress * Mathf.PI) * 105f;
                _rightArm.localRotation = Quaternion.Euler(-swingAngle, 0f, -12f);
            }

            var bob = _speed > 0.08f ? Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.035f : Mathf.Sin(_walkPhase) * 0.012f;
            _rig.localPosition = new Vector3(0f, bob, 0f);
            _rig.localRotation = Quaternion.identity;
            UpdateTint();
        }

        /// <summary>Starts a one-shot attack swing and slash flash.</summary>
        public void PlayAttack()
        {
            if (_dead || Time.time < _attackUntil)
            {
                return;
            }

            _attackStarted = Time.time;
            _attackUntil = Time.time + 0.42f;
            var socket = Weapons != null ? Weapons.EquippedTransform : null;
            var forward = _facing != null ? _facing.forward : transform.forward;
            var origin = socket != null ? socket.position : transform.position + (forward * 0.5f);
            CombatVfx.AttackFlash(origin + (forward * 0.35f),
                _facing != null ? _facing.rotation : transform.rotation,
                new Color(1f, 0.86f, 0.48f));
        }

        /// <summary>Red damage flash and a small recoil.</summary>
        public void PlayHit()
        {
            if (_dead)
            {
                return;
            }

            _flashUntil = Time.time + 0.18f;
        }

        /// <summary>World-space point used by gather and damage effects.</summary>
        public Vector3 VfxPoint => transform.position + (Vector3.up * (_height * 0.55f));

        private void UpdateTint()
        {
            var flashing = Time.time < _flashUntil;
            if (!flashing && _tint == null)
            {
                return;
            }

            if (_tint == null)
            {
                _tint = new MaterialPropertyBlock();
            }

            for (var i = 0; i < _bodyRenderers.Count; i++)
            {
                var renderer = _bodyRenderers[i];
                if (renderer == null) continue;
                if (!flashing)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }

                var baseColor = _baseColors[i];
                var flash = Color.Lerp(baseColor, new Color(1f, 0.12f, 0.08f, 1f), 0.72f);
                _tint.SetColor(BaseColor, flash);
                _tint.SetColor(EmissionColor, new Color(0.32f, 0.01f, 0.01f));
                renderer.SetPropertyBlock(_tint);
            }

            if (!flashing)
            {
                _tint = null;
            }
        }

        private void ResetPose()
        {
            _walkPhase = 0f;
            if (_rig != null)
            {
                _rig.localRotation = Quaternion.identity;
                _rig.localPosition = Vector3.zero;
            }
            if (_leftArm != null) _leftArm.localRotation = _restArm.Count > 0 ? _restArm[0] : Quaternion.identity;
            if (_rightArm != null) _rightArm.localRotation = _restArm.Count > 1 ? _restArm[1] : Quaternion.identity;
            if (_leftLeg != null) _leftLeg.localRotation = Quaternion.identity;
            if (_rightLeg != null) _rightLeg.localRotation = Quaternion.identity;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        private struct Palette
        {
            public Color Skin;
            public Color Cloth;
            public Color Leather;
            public Color Metal;
            public Color Accent;
            public Color Hair;
            public Color Dark;
            public Color Bone;

            public static Palette For(ProceduralCharacterProfile profile)
            {
                switch (profile)
                {
                    case ProceduralCharacterProfile.BlueMob:
                        return new Palette
                        {
                            Skin = new Color(0.52f, 0.66f, 0.72f),
                            Cloth = new Color(0.20f, 0.34f, 0.44f),
                            Leather = new Color(0.24f, 0.3f, 0.26f),
                            Metal = new Color(0.46f, 0.56f, 0.62f),
                            Accent = new Color(0.35f, 0.78f, 0.82f),
                            Hair = new Color(0.12f, 0.17f, 0.20f),
                            Dark = new Color(0.1f, 0.14f, 0.16f),
                            Bone = new Color(0.82f, 0.8f, 0.68f),
                        };
                    case ProceduralCharacterProfile.YellowMob:
                        return new Palette
                        {
                            Skin = new Color(0.68f, 0.51f, 0.35f),
                            Cloth = new Color(0.48f, 0.32f, 0.16f),
                            Leather = new Color(0.3f, 0.22f, 0.14f),
                            Metal = new Color(0.64f, 0.55f, 0.35f),
                            Accent = new Color(0.92f, 0.68f, 0.24f),
                            Hair = new Color(0.16f, 0.11f, 0.08f),
                            Dark = new Color(0.16f, 0.11f, 0.07f),
                            Bone = new Color(0.86f, 0.82f, 0.65f),
                        };
                    case ProceduralCharacterProfile.RedMob:
                        return new Palette
                        {
                            Skin = new Color(0.58f, 0.28f, 0.25f),
                            Cloth = new Color(0.30f, 0.10f, 0.11f),
                            Leather = new Color(0.2f, 0.09f, 0.09f),
                            Metal = new Color(0.48f, 0.31f, 0.23f),
                            Accent = new Color(0.9f, 0.26f, 0.16f),
                            Hair = new Color(0.12f, 0.06f, 0.05f),
                            Dark = new Color(0.1f, 0.04f, 0.04f),
                            Bone = new Color(0.88f, 0.78f, 0.58f),
                        };
                    default:
                        return new Palette
                        {
                            Skin = new Color(0.82f, 0.65f, 0.51f),
                            Cloth = new Color(0.22f, 0.44f, 0.52f),
                            Leather = new Color(0.31f, 0.22f, 0.15f),
                            Metal = new Color(0.58f, 0.64f, 0.68f),
                            Accent = new Color(0.92f, 0.72f, 0.28f),
                            Hair = new Color(0.18f, 0.12f, 0.08f),
                            Dark = new Color(0.1f, 0.12f, 0.15f),
                            Bone = new Color(0.9f, 0.85f, 0.7f),
                        };
                }
            }
        }
    }
}
