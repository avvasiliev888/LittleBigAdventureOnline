using System;
using System.Collections.Generic;
using System.Linq;
using Albion.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Albion.Editor
{
    /// <summary>
    /// Generic animation inspector: loads any model (FBX or prefab), plays any clip on it,
    /// and can retarget a Humanoid clip from another file onto that model.
    /// Read-only — it never writes assets, never touches a user scene and never enters Play Mode.
    ///
    /// Menu: Albion/Animation/Preview Window   (Ctrl+Shift+P)
    /// </summary>
    public sealed class AnimationPreviewWindow : EditorWindow
    {
        private const string PreviewRootName = "[Animation Preview]";

        // ── Model ────────────────────────────────────────────────────────────────
        private GameObject _modelAsset;
        private GameObject _instance;
        private Animator _animator;
        private Avatar _modelAvatar;
        private bool _modelIsHumanoid;
        private Bounds _modelBounds;
        private bool _hasBounds;

        // ── Clips ────────────────────────────────────────────────────────────────
        private readonly List<AnimationClip> _clips = new List<AnimationClip>();
        private string[] _clipLabels = Array.Empty<string>();
        private int _clipIndex = -1;
        private AnimationClip _activeClip;

        // ── Retarget source ──────────────────────────────────────────────────────
        private bool _showRetarget;
        private readonly List<GameObject> _sources = new List<GameObject>();
        private string[] _sourceLabels = Array.Empty<string>();
        private int _sourceIndex = -1;
        private AnimationClip _retargetClip;

        // ── Equipment ────────────────────────────────────────────────────────────
        private bool _showEquipment;
        private GameObject _weaponAsset;
        private GameObject _offhandAsset;
        private HandSlot _weaponSlot = HandSlot.RightHand;
        private HandSlot _offhandSlot = HandSlot.LeftHand;
        private GameObject _weaponInstance;
        private GameObject _offhandInstance;
        private Vector3 _weaponPosition;
        private Vector3 _weaponEuler;
        private float _weaponScale = 1f;
        private Vector3 _offhandPosition;
        private Vector3 _offhandEuler;
        private float _offhandScale = 1f;
        private bool _showHandMarkers;
        private bool _showAxes;
        private float _aimYaw;
        private float _headTurn;
        private float _weaponTwist;
        private float _offhandTwist;
        private Vector3 _weaponPropEuler;
        private Vector3 _offhandPropEuler;
        /// <summary>Authored local transforms, captured at mount. The per-frame wrist roll resets
        /// to these before applying, so the correction can never accumulate.</summary>
        private Vector3 _weaponAuthoredLocalPosition;
        private Quaternion _weaponAuthoredLocalRotation = Quaternion.identity;
        private Vector3 _offhandAuthoredLocalPosition;
        private Quaternion _offhandAuthoredLocalRotation = Quaternion.identity;

        private enum HandSlot { RightHand, LeftHand, Spine, None }

        // ── Playback ─────────────────────────────────────────────────────────────
        private bool _playing = true;
        private bool _looping = true;
        private float _speed = 1f;
        private float _time;
        private bool _showSkeleton;
        private bool _grounded;

        // ── View ─────────────────────────────────────────────────────────────────
        private float _yaw = 165f;
        private float _pitch = 6f;
        private float _zoom = 1f;
        private bool _dragging;

        // ── Render ───────────────────────────────────────────────────────────────
        private PreviewRenderUtility _preview;
        private Material _skeletonMaterial;
        private GUIStyle _hudStyle;
        private string _status = "Выберите модель и нажмите «Загрузить».";
        private MessageType _statusType = MessageType.None;

        [MenuItem("Albion/Animation/Preview Window %#p")]
        public static void Open()
        {
            var window = GetWindow<AnimationPreviewWindow>("Anim Preview");
            window.minSize = new Vector2(420f, 600f);
            window.Show();
        }

        /// <summary>The one click the user actually needs: warrior model + its attack state.</summary>
        [MenuItem("Albion/Animation/Preview Warrior Attack")]
        public static void PreviewWarriorAttack()
        {
            var window = GetWindow<AnimationPreviewWindow>("Anim Preview");
            window.minSize = new Vector2(420f, 600f);
            window.Show();
            window.LoadWarriorDefaults();
        }

        private void LoadWarriorDefaults()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AlbionAnimationSetup.ModelPath);
            if (model == null)
            {
                SetStatus("Модель не найдена: " + AlbionAnimationSetup.ModelPath, MessageType.Error);
                return;
            }

            _modelAsset = model;
            LoadModel();
            if (_clips.Count == 0) return;

            // The warrior attacks through the "Attack" state of WarriorLocomotion.controller.
            var index = IndexOfClip("Attack");
            if (index < 0) index = FindClipContaining("Attack");
            if (index < 0) index = FindClipContaining("Chop");
            SelectClip(index < 0 ? 0 : index);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            _preview = new PreviewRenderUtility();
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.13f, 0.14f, 0.16f, 1f);
            _preview.camera.fieldOfView = 32f;
            _preview.camera.nearClipPlane = 0.05f;
            _preview.camera.farClipPlane = 200f;
            _preview.ambientColor = new Color(0.45f, 0.47f, 0.52f);
            _preview.lights[0].intensity = 1.15f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            if (_preview.lights.Length > 1)
            {
                _preview.lights[1].intensity = 0.45f;
                _preview.lights[1].transform.rotation = Quaternion.Euler(15f, 145f, 0f);
            }
        }

        private void OnDisable()
        {
            UnloadModel();
            _preview?.Cleanup();
            _preview = null;
            if (_skeletonMaterial != null) DestroyImmediate(_skeletonMaterial);
            _skeletonMaterial = null;
            if (_groundMaterial != null) DestroyImmediate(_groundMaterial);
            _groundMaterial = null;
            if (_markerMaterial != null) DestroyImmediate(_markerMaterial);
            _markerMaterial = null;
        }

        private void Update()
        {
            if (_instance != null && _playing)
            {
                Advance(Time.realtimeSinceStartup - _lastTick);
                Repaint();
            }
            _lastTick = Time.realtimeSinceStartup;
            ApplyAimYaw();
        }

        /// <summary>
        /// Turns the whole model so the archer aims where the clip points him. Applied to the root
        /// rather than to a bone: a bone edit would be overwritten by the clip on the next sample.
        /// </summary>
        private void ApplyAimYaw()
        {
            if (_instance == null) return;
            var rotation = Quaternion.Euler(0f, _aimYaw, 0f);
            if (_instance.transform.rotation != rotation) _instance.transform.rotation = rotation;
        }

        private float _lastTick;

        private void OnInspectorUpdate() => Repaint();

        // ── GUI ──────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawHeader();
            DrawPlayback();
            DrawEquipment();
            DrawRetarget();
            DrawView();
            DrawViewport();
        }

        private void DrawEquipment()
        {
            if (_instance == null) return;
            _showEquipment = EditorGUILayout.Foldout(_showEquipment, "Оружие в руках", true);
            if (!_showEquipment) return;

            EditorGUILayout.HelpBox(
                "Киньте FBX или prefab оружия. Меч/топор/щит воина лежат отдельными файлами в " +
                "dRealWarriorFantasyCharacter/Meshes; лук — Art/Weapons/Generated/bow.prefab.",
                MessageType.None);

            DrawArmSlot("Правая рука (основное)", true, ref _weaponAsset, ref _weaponSlot,
                ref _weaponPosition, ref _weaponEuler, ref _weaponPropEuler, ref _weaponScale, ref _weaponTwist);
            DrawArmSlot("Левая рука (второе)", false, ref _offhandAsset, ref _offhandSlot,
                ref _offhandPosition, ref _offhandEuler, ref _offhandPropEuler, ref _offhandScale, ref _offhandTwist);

            using (new EditorGUILayout.HorizontalScope())
            {
                _showHandMarkers = EditorGUILayout.ToggleLeft("Маркеры кистей", _showHandMarkers, GUILayout.Width(150f));
                if (GUILayout.Button("Меч + щит")) ApplyWarriorPreset();
                if (GUILayout.Button("Лук")) ApplyBowPreset();
                if (GUILayout.Button("Сброс")) ResetEquipment();
            }
        }

        /// <summary>
        /// Sword and shield as hand-calibrated by the operator in the preview window and saved to
        /// Resources/Characters/WarriorVisual.asset. Sword: (-0.01, -0.14, -0.04) / (0, 0, 90).
        /// Shield: (0, 0.13, 0.05) / (205.661, 1.959, 90.1). The vendor FBX pivots are not at the
        /// grip, so these numbers are the calibration, not a guess.
        /// </summary>
        private void ApplyWarriorPreset()
        {
            _weaponAsset = LoadDefault("Assets/dRealWarriorFantasyCharacter/Meshes/Warrior_Sword.fbx");
            _weaponSlot = HandSlot.RightHand;
            _weaponPosition = new Vector3(-0.01f, -0.14f, -0.04f);
            _weaponEuler = new Vector3(0f, 0f, 90f);
            _weaponScale = 1f;

            _offhandAsset = LoadDefault("Assets/dRealWarriorFantasyCharacter/Meshes/Warrior_Shield.fbx");
            _offhandSlot = HandSlot.LeftHand;
            _offhandPosition = new Vector3(0f, 0.13f, 0.05f);
            _offhandEuler = new Vector3(205.661f, 1.959f, 90.1f);
            _offhandScale = 1f;

            RebuildEquipment();
            SetStatus("Меч и щит в руках (ваша калибровка).", MessageType.None);
        }

        // ── Bow calibration (hand-tuned by the operator, do not "improve") ────────
        // Bow sits in the LEFT hand. Numbers below reproduce the exact pose signed off in the
        // preview: bow prop placement, the wrist roll that turns the palm under the grip, the
        // root turn that aligns the shot with the body, and the head turn that faces the bow
        // with 10 deg of slack.
        private static readonly Vector3 BowPosition = new Vector3(0.09f, 0.12f, -0.15f);
        private static readonly Vector3 BowEuler = new Vector3(90f, 90f, -95f);
        private const float BowScale = 1f;
        private const float BowHandRoll = 90f;
        private const float BowAimYaw = 5.546f;
        private const float BowHeadTurn = -82.16f;

        /// <summary>Restores the signed-off bow pose: prop, wrist roll, aim yaw and head turn.</summary>
        private void ApplyBowPreset()
        {
            _weaponAsset = LoadDefault("Assets/Art/Weapons/Generated/bow.prefab");
            _weaponSlot = HandSlot.LeftHand;
            _weaponPosition = BowPosition;
            _weaponEuler = BowEuler;
            _weaponScale = BowScale;
            _weaponTwist = BowHandRoll;
            _weaponPropEuler = Vector3.zero;

            _aimYaw = BowAimYaw;
            _headTurn = BowHeadTurn;

            _offhandAsset = null;
            if (_offhandInstance != null) { DestroyImmediate(_offhandInstance); _offhandInstance = null; }

            RebuildEquipment();
            SetStatus("Лук: ваша калибровка восстановлена (кисть " + BowHandRoll
                + "°, голова " + BowHeadTurn + "°).", MessageType.None);
        }

        private void ResetEquipment()
        {
            _weaponAsset = null;
            _offhandAsset = null;
            _weaponPosition = Vector3.zero;
            _weaponEuler = Vector3.zero;
            _weaponScale = 1f;
            _weaponTwist = 0f;
            _weaponPropEuler = Vector3.zero;
            _offhandPosition = Vector3.zero;
            _offhandEuler = Vector3.zero;
            _offhandScale = 1f;
            _offhandTwist = 0f;
            _offhandPropEuler = Vector3.zero;
            RebuildEquipment();
        }


        /// <summary>
        /// Edits one arm slot. The twist lives here rather than in a separate global slider so it
        /// can never address the wrong hand: whichever slot holds the bow is the one being spun.
        /// </summary>
        private void DrawArmSlot(string label, bool isMain, ref GameObject asset, ref HandSlot slot,
            ref Vector3 position, ref Vector3 euler, ref Vector3 propEuler, ref float scale, ref float twist)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            var changed = false;
            EditorGUI.BeginChangeCheck();
            asset = (GameObject)EditorGUILayout.ObjectField("Модель", asset, typeof(GameObject), false);
            slot = (HandSlot)EditorGUILayout.EnumPopup("Кость", slot);
            position = EditorGUILayout.Vector3Field("Смещение", position);
            euler = EditorGUILayout.Vector3Field("Поворот", euler);
            scale = EditorGUILayout.Slider("Масштаб", scale, 0.01f, 10f);
            if (EditorGUI.EndChangeCheck()) changed = true;

            // Turns the prop itself around its grip, independent of the hand. This is the control
            // for "the bow still needs more rotation" — it does not move the arm at all.
            EditorGUI.BeginChangeCheck();
            propEuler = EditorGUILayout.Vector3Field("Доворот оружия", propEuler);
            if (EditorGUI.EndChangeCheck()) changed = true;

            EditorGUI.BeginChangeCheck();
            twist = EditorGUILayout.Slider("Разворот кисти", twist, -180f, 180f);
            if (EditorGUI.EndChangeCheck()) changed = true;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Кисть +90°")) { twist += 90f; changed = true; }
                if (GUILayout.Button("Кисть −90°")) { twist -= 90f; changed = true; }
                if (GUILayout.Button("Сбросить")) { twist = 0f; propEuler = Vector3.zero; changed = true; }
            }

            if (!changed) return;
            if (isMain) RebuildWeapon(); else RebuildOffhand();
        }

        private static GameObject LoadDefault(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) Debug.LogWarning("[preview] Not found: " + path);
            return asset;
        }

        /// <summary>Resolves a hand bone on the preview instance; null when the rig is not Humanoid.</summary>
        private Transform ResolveBone(HandSlot slot)
        {
            if (_animator == null) return null;
            switch (slot)
            {
                case HandSlot.RightHand:
                    return _animator.isHuman
                        ? _animator.GetBoneTransform(HumanBodyBones.RightHand)
                        : FindNamed("RightHand", "R Hand", "hand.r");
                case HandSlot.LeftHand:
                    return _animator.isHuman
                        ? _animator.GetBoneTransform(HumanBodyBones.LeftHand)
                        : FindNamed("LeftHand", "L Hand", "hand.l");
                case HandSlot.Spine:
                    return _animator.isHuman
                        ? _animator.GetBoneTransform(HumanBodyBones.Chest)
                        : FindNamed("Spine1", "Chest", "Spine");
                default:
                    return null;
            }
        }

        private Transform FindNamed(params string[] names)
        {
            if (_instance == null) return null;
            foreach (var candidate in _instance.GetComponentsInChildren<Transform>(true))
                foreach (var name in names)
                    if (candidate.name == name) return candidate;
            return null;
        }

        private void RebuildEquipment()
        {
            RebuildWeapon();
            RebuildOffhand();
        }

        private void RebuildWeapon()
        {
            Mount(ref _weaponInstance, _weaponAsset, _weaponSlot, _weaponPosition,
                _weaponEuler + _weaponPropEuler, _weaponScale);
            CaptureAuthored(_weaponInstance, ref _weaponAuthoredLocalPosition, ref _weaponAuthoredLocalRotation);
            Sample();
        }

        private void RebuildOffhand()
        {
            Mount(ref _offhandInstance, _offhandAsset, _offhandSlot, _offhandPosition,
                _offhandEuler + _offhandPropEuler, _offhandScale);
            CaptureAuthored(_offhandInstance, ref _offhandAuthoredLocalPosition, ref _offhandAuthoredLocalRotation);
            Sample();
        }

        /// <summary>Records the mount transform the wrist-roll correction must return to.</summary>
        private static void CaptureAuthored(GameObject weapon, ref Vector3 position, ref Quaternion rotation)
        {
            if (weapon == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            position = weapon.transform.localPosition;
            rotation = weapon.transform.localRotation;
        }

        /// <summary>Instantiates weapon geometry under a bone and strips its own colliders.</summary>
        private void Mount(ref GameObject instance, GameObject asset, HandSlot slot,
            Vector3 position, Vector3 euler, float scale)
        {
            if (instance != null)
            {
                DestroyImmediate(instance);
                instance = null;
            }
            if (asset == null || _instance == null) return;

            var parent = ResolveBone(slot);
            if (parent == null)
            {
                SetStatus("Кость для оружия не найдена — проверьте, что риг Humanoid.", MessageType.Warning);
                return;
            }

            instance = Instantiate(asset, parent, false);
            instance.name = "(preview) " + asset.name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(euler);
            instance.transform.localScale = Vector3.one * scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _modelAsset = (GameObject)EditorGUILayout.ObjectField("Модель", _modelAsset, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck() && _modelAsset != null) LoadModel();

                using (new EditorGUI.DisabledScope(_modelAsset == null))
                {
                    if (GUILayout.Button("Загрузить", GUILayout.Width(84f))) LoadModel();
                }
            }

            using (new EditorGUI.DisabledScope(_instance == null))
            {
                if (_clips.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        _instance == null
                            ? "Модель не загружена."
                            : "В модели нет клипов. Откройте раздел «Ретаргет» и выберите источник.",
                        MessageType.Warning);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var index = EditorGUILayout.Popup("Клип", Mathf.Max(0, _clipIndex), _clipLabels);
                    if (index != _clipIndex) SelectClip(index);
                    using (new EditorGUI.DisabledScope(_clipIndex <= 0))
                    {
                        if (GUILayout.Button("◀", GUILayout.Width(26f))) SelectClip(_clipIndex - 1);
                    }
                    using (new EditorGUI.DisabledScope(_clipIndex >= _clipLabels.Length - 1))
                    {
                        if (GUILayout.Button("▶", GUILayout.Width(26f))) SelectClip(_clipIndex + 1);
                    }
                }

                if (_activeClip != null)
                {
                    var frames = Mathf.Max(1, Mathf.RoundToInt(_activeClip.length * _activeClip.frameRate));
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(
                            "Длина / кадры",
                            _activeClip.length.ToString("0.00") + " с  ·  " + frames + " кадров  ·  " +
                            (_activeClip.humanMotion ? "Humanoid" : "Generic"));
                    }
                }
            }
        }

        private void DrawPlayback()
        {
            if (_instance == null) return;
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "⏸ Пауза" : "▶ Играть")) _playing = !_playing;
                if (GUILayout.Button("⏮ В начало")) { _time = 0f; _playing = false; Sample(); }
                if (GUILayout.Button("− кадр")) StepFrames(-1);
                if (GUILayout.Button("+ кадр")) StepFrames(1);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _looping = EditorGUILayout.ToggleLeft("Цикл", _looping, GUILayout.Width(72f));
                _showSkeleton = EditorGUILayout.ToggleLeft("Скелет", _showSkeleton, GUILayout.Width(82f));
                _grounded = EditorGUILayout.ToggleLeft("На полу", _grounded, GUILayout.Width(82f));
            }

            _speed = EditorGUILayout.Slider("Скорость", _speed, 0.05f, 2f);

            var length = Mathf.Max(0.0001f, ClipLength);
            EditorGUI.BeginChangeCheck();
            var scrubbed = EditorGUILayout.Slider("Время", _time, 0f, length);
            if (EditorGUI.EndChangeCheck())
            {
                _playing = false;
                _time = scrubbed;
                Sample();
            }

            var fps = _activeClip != null ? Mathf.Max(1f, _activeClip.frameRate) : 30f;
            var current = Mathf.RoundToInt(_time * fps);
            var total = Mathf.Max(1, Mathf.RoundToInt(length * fps));
            EditorGUILayout.LabelField("Кадр", current + " / " + total, EditorStyles.miniBoldLabel);
        }

        private void DrawRetarget()
        {
            _showRetarget = EditorGUILayout.Foldout(_showRetarget, "Ретаргет: клип из другого файла", true);
            if (!_showRetarget) return;

            if (_sources.Count == 0 && GUILayout.Button("Найти Humanoid-источники в Assets"))
                RebuildSources();

            if (_sources.Count == 0)
            {
                EditorGUILayout.HelpBox("Источники не найдены. Нажмите кнопку выше.", MessageType.None);
                return;
            }

            var picked = EditorGUILayout.Popup("Источник", Mathf.Max(0, _sourceIndex), _sourceLabels);
            if (picked != _sourceIndex)
            {
                _sourceIndex = picked;
                RebuildSourceClips();
            }

            if (_retargetClip != null && GUILayout.Button("Применить к модели"))
            {
                ApplyRetarget(_retargetClip);
            }

            if (_retargetClip != null)
            {
                var frames = Mathf.Max(1, Mathf.RoundToInt(_retargetClip.length * _retargetClip.frameRate));
                EditorGUILayout.LabelField("Клип", _retargetClip.name + "  ·  " + frames + " кадров");
            }

            if (!_modelIsHumanoid)
            {
                EditorGUILayout.HelpBox(
                    "Модель не Humanoid — ретаргет невозможен. Поставьте Rig = Humanoid в импортёре модели.",
                    MessageType.Warning);
            }
        }

        private void DrawView()
        {
            if (_instance == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Спереди")) { _yaw = 0f; _pitch = 4f; _zoom = 1f; }
                if (GUILayout.Button("Профиль")) { _yaw = 90f; _pitch = 4f; _zoom = 1f; }
                if (GUILayout.Button("3/4")) { _yaw = 165f; _pitch = 8f; _zoom = 1f; }
                if (GUILayout.Button("Сверху")) { _yaw = 0f; _pitch = 88f; _zoom = 1f; }
            }

            _yaw = EditorGUILayout.Slider("Поворот", _yaw, -180f, 180f);
            _pitch = EditorGUILayout.Slider("Наклон", _pitch, -35f, 88f);
            _zoom = EditorGUILayout.Slider("Зум", _zoom, 0.4f, 4f);

            _showAxes = EditorGUILayout.ToggleLeft(
                "Оси: вперёд (синяя), влево (красная) и куда смотрит голова", _showAxes);
            EditorGUI.BeginChangeCheck();
            _aimYaw = EditorGUILayout.Slider("Поворот модели", _aimYaw, -180f, 180f);
            _headTurn = EditorGUILayout.Slider("Поворот головы", _headTurn, -120f, 120f);
            // While paused nothing re-samples, so the slider would otherwise look dead.
            if (EditorGUI.EndChangeCheck()) Sample();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Голова к луку")) AimHeadAtBow();
                if (GUILayout.Button("Голова −10°")) { _headTurn -= 10f; Sample(); }
                if (GUILayout.Button("Сбросить развороты"))
                {
                    _aimYaw = 0f;
                    _headTurn = 0f;
                    Sample();
                }
            }
            DrawAimReport();
        }

        /// <summary>
        /// Reports where an arrow would actually fly.
        ///
        /// Deliberately uses BONE POSITIONS, never bone local axes: Mixamo and dReal both ship
        /// skeletons whose bone axes are rolled relative to the body, so hips.forward reads ~90 deg
        /// off no matter how correct the pose is. Positions are rig-agnostic.
        ///   torso forward  = right shoulder -> left shoulder, crossed with up
        ///   shot line      = draw hand -> bow hand
        /// The angle between them says whether the arms point forward (archer) or sideways (broken).
        /// </summary>
        private void DrawAimReport()
        {
            if (_animator == null || !_animator.isHuman) return;

            var leftShoulder = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightShoulder = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (leftShoulder == null || rightShoulder == null) return;

            var right = rightShoulder.position - leftShoulder.position;
            var torsoForward = Vector3.Cross(right, Vector3.up);
            var torsoYaw = Yaw(torsoForward);

            var shoulder = leftHand != null && rightHand != null
                ? leftHand.position - rightHand.position
                : Vector3.zero;
            var shotYaw = Yaw(shoulder);
            var armOffset = Mathf.DeltaAngle(torsoYaw, shotYaw);
            // Where the arrow goes relative to the model's own forward axis. This is the number
            // that decides whether shooting looks right in game, and it is what a root Yaw fixes.
            var shotVsModel = Mathf.DeltaAngle(_aimYaw, shotYaw);

            // The head is what the eye actually judges: "does he look where the bow points?"
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            var headYaw = head != null ? Yaw(head.forward) : 0f;
            var headVsShot = head != null ? Mathf.DeltaAngle(headYaw, shotYaw) : 0f;

            EditorGUILayout.LabelField(string.Format(
                "ГОЛОВА vs ЛУК: {0:0.#}°   (0 = смотрит точно вдоль лука)",
                headVsShot), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.Format(
                "Корпус: {0:0.#}°   Линия выстрела: {1:0.#}°   Стрела к модели: {2:0.#}°",
                torsoYaw, shotYaw, shotVsModel), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(string.Format(
                "Руки к корпусу: {0:0.#}°   Поворот головы: {1:0.#}°   Размах рук: {2:0.00} м",
                armOffset, _headTurn, shoulder.magnitude), EditorStyles.miniLabel);

            if (Mathf.Abs(headVsShot) > 20f)
            {
                EditorGUILayout.HelpBox(
                    "Голова смотрит мимо лука на " + Mathf.Abs(headVsShot).ToString("0.#")
                    + "°. Нажмите «Голова к луку» или подкрутите слайдер «Поворот головы».",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Голова смотрит вдоль лука (расхождение " + Mathf.Abs(headVsShot).ToString("0.#")
                    + "°). Стойка согласована.", MessageType.Info);
            }

            // An archer stands side-on: the chest is turned away from the target while the bow arm
            // reaches toward it, so ~90 deg between chest and bow arm is the CORRECT stance, not a
            // fault. Only the head-vs-bow number above is worth acting on.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Навести на линию выстрела"))
                {
                    // Measurements are world space and already include _aimYaw, so the extra turn
                    // needed to bring the shot line to 0 is simply minus the measured angle.
                    _aimYaw = Mathf.DeltaAngle(0f, _aimYaw - shotYaw);
                    SetStatus("Модель развёрнута по линии выстрела (" + shotYaw.ToString("0.#") + "°).",
                        MessageType.None);
                }
                if (GUILayout.Button("Навести по корпусу"))
                {
                    _aimYaw = Mathf.DeltaAngle(0f, _aimYaw - torsoYaw);
                    SetStatus("Модель развёрнута по корпусу (" + torsoYaw.ToString("0.#") + "°).",
                        MessageType.None);
                }
            }
        }

        private static float Yaw(Vector3 direction)
        {
            var flat = new Vector3(direction.x, 0f, direction.z);
            return flat.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Turns the head toward the bow, stopping short by <see cref="GazeSlack"/> degrees.
        /// A perfectly aligned head reads as rigid; a few degrees of slack is what makes an archer
        /// look like he is tracking a target rather than staring down the shaft.
        /// </summary>
        private void AimHeadAtBow()
        {
            if (_animator == null || !_animator.isHuman) return;
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            var leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (head == null || leftHand == null || rightHand == null) return;

            var bowYaw = Yaw(leftHand.position - rightHand.position);
            var headYaw = Yaw(head.forward);
            var delta = Mathf.DeltaAngle(headYaw, bowYaw);
            if (Mathf.Abs(delta) > GazeSlack)
                delta -= Mathf.Sign(delta) * GazeSlack;

            _headTurn = Mathf.Clamp(_headTurn + delta, -120f, 120f);
            Sample();
            SetStatus("Голова повёрнута к луку, недоворот " + GazeSlack.ToString("0")
                + "° (итого " + _headTurn.ToString("0.#") + "°).", MessageType.None);
        }

        /// <summary>Degrees of deliberate under-rotation when aiming the head at the bow.</summary>
        private const float GazeSlack = 10f;

        private void DrawViewport()
        {
            if (_preview == null) return;

            var viewportHeight = Mathf.Max(180f, position.height - 420f);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(viewportHeight), GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
                Render(rect);

            HandleOrbit(rect);

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
                _hudStyle.normal.textColor = new Color(0.92f, 0.94f, 0.98f);
            }

            var hud = string.IsNullOrEmpty(_activeClip?.name) ? "—" : _activeClip.name;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 20f), hud, _hudStyle);
            GUI.Label(new Rect(rect.x + 8f, rect.yMax - 22f, rect.width - 16f, 18f), _status, EditorStyles.miniLabel);
        }

        private void HandleOrbit(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _dragging = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                _dragging = false;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _yaw += e.delta.x * 0.55f;
                _pitch = Mathf.Clamp(_pitch - e.delta.y * 0.4f, -35f, 88f);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                _zoom = Mathf.Clamp(_zoom * (1f + e.delta.y * 0.04f), 0.4f, 4f);
                e.Use();
                Repaint();
            }
        }

        private void Render(Rect rect)
        {
            if (rect.width < 1f || rect.height < 1f) return;

            var center = _hasBounds ? _modelBounds.center : Vector3.zero;
            var radius = _hasBounds
                ? Mathf.Max(0.35f, _modelBounds.extents.magnitude)
                : 1f;

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var distance = radius * 2.45f * _zoom;

            var camera = _preview.camera;
            camera.transform.position = center + rotation * Vector3.forward * distance;
            camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
            camera.aspect = rect.width / rect.height;

            _preview.BeginPreview(rect, GUIStyle.none);
            try
            {
                if (_grounded) DrawGround(center, radius);
                if (_showSkeleton) DrawSkeleton();
                if (_showHandMarkers) DrawHandMarkers();
                if (_showAxes) DrawAxes();

                _preview.camera.Render();
                var texture = _preview.EndPreview();
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            }
            catch (Exception error)
            {
                SetStatus("Ошибка рендера: " + error.Message, MessageType.Error);
            }
        }

        private void DrawGround(Vector3 center, float radius)
        {
            var size = Mathf.Max(2f, radius * 4f);
            var y = (_hasBounds ? _modelBounds.min.y : 0f) + 0.002f;
            var matrix = Matrix4x4.Translate(new Vector3(center.x, y, center.z));
            _preview.DrawMesh(MeshOf.Grid(size), matrix, GroundMaterial, 0);
        }

        private Material _groundMaterial;

        private Material GroundMaterial
        {
            get
            {
                if (_groundMaterial == null)
                    _groundMaterial = MaterialOf.Unlit(new Color(0.30f, 0.33f, 0.38f, 1f));
                return _groundMaterial;
            }
        }

        /// <summary>World-space aim gizmo: blue = body forward, red = body left, yellow = head forward.</summary>
        private void DrawAxes()
        {
            if (_instance == null) return;
            var material = MarkerMaterial;
            var root = _instance.transform;
            var origin = root.position + Vector3.up * (root.position.y + 0.02f);
            var length = _hasBounds ? Mathf.Max(0.5f, _modelBounds.size.y * 0.5f) : 1f;
            var thickness = length * 0.012f;

            DrawArrow(origin, root.forward * length, thickness, material);
            DrawArrow(origin, -root.right * length * 0.6f, thickness, material);

            if (_animator != null && _animator.isHuman)
            {
                var head = _animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) DrawArrow(head.position, head.forward * length * 0.7f, thickness, material);
            }
        }

        private void DrawArrow(Vector3 from, Vector3 delta, float thickness, Material material)
        {
            var to = from + delta;
            _preview.DrawMesh(MeshOf.Line(from, to, thickness), Matrix4x4.identity, material, 0);
            _preview.DrawMesh(MeshOf.Octahedron,
                Matrix4x4.TRS(to, Quaternion.LookRotation(delta.normalized), Vector3.one * thickness * 3.5f),
                material, 0);
        }

        /// <summary>Marks the equipped hand bones so weapon placement is verifiable at a glance.</summary>
        private void DrawHandMarkers()
        {
            if (_animator == null) return;
            var material = MarkerMaterial;
            var size = _hasBounds ? Mathf.Max(0.015f, _modelBounds.size.magnitude * 0.01f) : 0.03f;

            foreach (var slot in new[] { HandSlot.RightHand, HandSlot.LeftHand })
            {
                var bone = ResolveBone(slot);
                if (bone == null) continue;
                var matrix = Matrix4x4.TRS(bone.position, bone.rotation, Vector3.one * size);
                _preview.DrawMesh(MeshOf.Octahedron, matrix, material, 0);
                _preview.DrawMesh(MeshOf.Line(bone.position, bone.position + bone.forward * size * 6f, size * 0.25f),
                    Matrix4x4.identity, material, 0);
            }
        }

        private Material _markerMaterial;

        private Material MarkerMaterial
        {
            get
            {
                if (_markerMaterial == null)
                    _markerMaterial = MaterialOf.Unlit(new Color(1f, 0.35f, 0.25f, 1f));
                return _markerMaterial;
            }
        }

        private void DrawSkeleton()
        {
            if (_animator == null || !_animator.isHuman) return;
            var material = SkeletonMaterial;
            var size = _hasBounds ? Mathf.Max(0.012f, _modelBounds.size.magnitude * 0.008f) : 0.02f;

            foreach (var bone in _animator.GetComponentsInChildren<Transform>(true))
            {
                if (bone == _animator.transform) continue;
                var matrix = Matrix4x4.TRS(bone.position, bone.rotation, Vector3.one * size * 0.6f);
                _preview.DrawMesh(MeshOf.Octahedron, matrix, material, 0);
            }

            ConnectChain(new[]
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
                HumanBodyBones.Neck, HumanBodyBones.Head,
            }, material, size);

            ConnectChain(new[] { HumanBodyBones.Chest, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }, material, size);
            ConnectChain(new[] { HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand }, material, size);
            ConnectChain(new[] { HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }, material, size);
            ConnectChain(new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot }, material, size);
        }

        private void ConnectChain(HumanBodyBones[] chain, Material material, float size)
        {
            if (_animator == null || !_animator.isHuman) return;
            for (var i = 0; i + 1 < chain.Length; i++)
            {
                var from = _animator.GetBoneTransform(chain[i]);
                var to = _animator.GetBoneTransform(chain[i + 1]);
                if (from == null || to == null) continue;
                _preview.DrawMesh(MeshOf.Line(from.position, to.position, size * 0.35f), Matrix4x4.identity, material, 0);
            }
        }

        private Material SkeletonMaterial
        {
            get
            {
                if (_skeletonMaterial == null)
                    _skeletonMaterial = MaterialOf.Unlit(new Color(0.35f, 1f, 0.75f, 1f));
                return _skeletonMaterial;
            }
        }

        // ── Model loading ────────────────────────────────────────────────────────

        private void LoadModel()
        {
            UnloadModel();
            _clips.Clear();
            _clipLabels = Array.Empty<string>();
            _clipIndex = -1;
            _activeClip = null;

            if (_modelAsset == null)
            {
                SetStatus("Модель не выбрана.", MessageType.None);
                return;
            }

            var path = AssetDatabase.GetAssetPath(_modelAsset);
            _modelAvatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>()
                .FirstOrDefault(avatar => avatar.isValid);
            _modelIsHumanoid = _modelAvatar != null && _modelAvatar.isHuman;

            _instance = Instantiate(_modelAsset);
            _instance.name = _modelAsset.name + " (preview)";
            _instance.transform.position = Vector3.zero;
            _instance.transform.rotation = Quaternion.Euler(0f, _aimYaw, 0f);
            _instance.hideFlags = HideFlags.HideAndDontSave;
            DisableLogic(_instance);

            _preview.AddSingleGO(_instance);

            _animator = _instance.GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                // Raw FBX models ship without an Animator component; add one so clips can be
                // sampled on the imported avatar.
                _animator = _instance.GetComponent<Animator>();
                if (_animator == null) _animator = _instance.AddComponent<Animator>();
                _animator.avatar = _modelAvatar;
            }
            else if (_animator.avatar == null)
            {
                _animator.avatar = _modelAvatar;
            }

            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.enabled = true;

            // The warrior FBX has no embedded clips and no controller; the attack lives in a
            // separate controller asset. Attach it so its state clips become selectable.
            var controller = ResolvePreviewController(_modelAsset.name);
            if (controller != null) _animator.runtimeAnimatorController = controller;

            CollectClips(path);
            ComputeBounds();
            RebuildEquipment();

            if (_clips.Count > 0) SelectClip(0);

            SetStatus(
                _modelAsset.name +
                ( _animator == null ? " · без Animator" : " · Animator ok") +
                " · клипов " + _clips.Count +
                (_modelIsHumanoid ? " · Humanoid" : " · не Humanoid"),
                _clips.Count > 0 ? MessageType.None : MessageType.Warning);
        }

        private void UnloadModel()
        {
            if (_weaponInstance != null) { DestroyImmediate(_weaponInstance); _weaponInstance = null; }
            if (_offhandInstance != null) { DestroyImmediate(_offhandInstance); _offhandInstance = null; }
            if (_instance != null)
            {
                // PreviewRenderUtility owns the instance once AddSingleGO was called, so the
                // window's Cleanup() (in OnDisable) releases it. Destroying it here as well
                // would leave a dangling reference inside the utility.
                DestroyImmediate(_instance);
            }
            _instance = null;
            _animator = null;
            _modelAvatar = null;
            _hasBounds = false;
        }

        /// <summary>
        /// Finds a controller to preview on a clip-less model: the project settings controller
        /// first, then any controller whose clips mention the model family, then any controller
        /// at all that carries attack states.
        /// </summary>
        private static RuntimeAnimatorController ResolvePreviewController(string modelName)
        {
            try
            {
                var settings = CharacterVisualSettings.Load();
                if (settings != null && settings.Controller != null) return settings.Controller;
            }
            catch (Exception)
            {
                // Settings not configured yet; fall through to the explicit controller path.
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AlbionAnimationSetup.ControllerPath);
            if (controller != null) return controller;

            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate == null) continue;
                if (candidate.animationClips.Any(clip => clip != null && clip.name.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0))
                    return candidate;
            }

            return null;
        }

        private void CollectClips(string modelPath)
        {
            var controller = _animator != null ? _animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                foreach (var clip in controller.animationClips)
                    if (IsUsableClip(clip) && !_clips.Contains(clip)) _clips.Add(clip);
            }

            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>())
                if (IsUsableClip(clip) && !_clips.Contains(clip)) _clips.Add(clip);

            _clipLabels = _clips.Select((clip, i) => (i + 1) + "  " + clip.name).ToArray();
        }

        private static bool IsUsableClip(AnimationClip clip) =>
            clip != null && !clip.name.StartsWith("__preview", StringComparison.Ordinal)
            && !clip.empty && clip.length > 0.0005f;

        private void ComputeBounds()
        {
            _hasBounds = false;
            if (_instance == null) return;
            var renderers = _instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            _modelBounds = bounds;
            _hasBounds = bounds.size.sqrMagnitude > 0.0001f;
        }

        private static void DisableLogic(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null) behaviour.enabled = false;
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                animator.applyRootMotion = false;
                animator.enabled = true;
            }
        }

        // ── Playback ─────────────────────────────────────────────────────────────

        private float ClipLength => _activeClip != null ? Mathf.Max(0.001f, _activeClip.length) : 0.001f;

        private void SelectClip(int index)
        {
            if (_clips.Count == 0) return;
            _clipIndex = Mathf.Clamp(index, 0, _clips.Count - 1);
            _activeClip = _clips[_clipIndex];
            _time = 0f;
            _playing = true;
            RebindAnimator();
            Sample();
        }

        /// <summary>Restarts the Animator so the new clip is not blended from the previous pose.</summary>
        private void RebindAnimator()
        {
            if (_animator == null) return;
            _animator.enabled = false;
            _animator.enabled = true;
            _animator.Rebind();
            _animator.Update(0f);
        }

        private void Advance(float delta)
        {
            if (_activeClip == null) return;
            _time += Mathf.Clamp(delta, 0f, 0.1f) * _speed;

            if (_time >= ClipLength)
            {
                if (_looping) _time = Mathf.Repeat(_time, ClipLength);
                else { _time = ClipLength - 0.0001f; _playing = false; }
            }

            Sample();
        }

        private void StepFrames(int frames)
        {
            if (_activeClip == null) return;
            var fps = Mathf.Max(1f, _activeClip.frameRate);
            _playing = false;
            _time = Mathf.Clamp(_time + frames / fps, 0f, ClipLength - 0.0001f);
            Sample();
        }

        /// <summary>
        /// Evaluates the active clip at the current time. SampleAnimation is the single source of
        /// truth: it writes the pose directly onto the instance and already performs Humanoid
        /// retargeting when the clip came from another avatar. Driving the Animator as well would
        /// fight it over the same transforms and require Play-by-state-name lookups.
        /// </summary>
        private void Sample()
        {
            if (_instance == null || _activeClip == null) return;
            var evalTime = Mathf.Clamp(_time, 0f, Mathf.Max(0f, ClipLength - 0.0001f));
            _activeClip.SampleAnimation(_instance, evalTime);
            ApplyHeadTurn();
            ApplyHandRolls();
        }

        /// <summary>
        /// Rolls the hand bone that holds a weapon while keeping the prop at its AUTHORED
        /// orientation relative to the clip's own hand pose.
        ///
        /// The authored local transform is restored before every evaluation. Without that reset the
        /// correction feeds its own output back in: pinning the prop in world space rewrites its
        /// LOCAL transform, the next sample resets the hand underneath it, and the prop drifts a
        /// little more each frame — which is exactly why nudging the value by one degree appeared to
        /// "fix" a pose that was already wrong at the value typed in.
        /// </summary>
        private void ApplyHandRolls()
        {
            if (_animator == null || !_animator.isHuman) return;
            ApplyHandRoll(_weaponSlot, _weaponInstance, _weaponTwist,
                _weaponAuthoredLocalPosition, _weaponAuthoredLocalRotation);
            ApplyHandRoll(_offhandSlot, _offhandInstance, _offhandTwist,
                _offhandAuthoredLocalPosition, _offhandAuthoredLocalRotation);
        }

        private void ApplyHandRoll(HandSlot slot, GameObject weapon, float degrees,
            Vector3 authoredPosition, Quaternion authoredRotation)
        {
            if (weapon == null) return;

            // Reset to the authored transform first, so this frame starts from a known state no
            // matter what the previous frame's correction left behind.
            weapon.transform.localPosition = authoredPosition;
            weapon.transform.localRotation = authoredRotation;
            if (Mathf.Abs(degrees) < 0.01f) return;

            HumanBodyBones handBone, forearmBone;
            switch (slot)
            {
                case HandSlot.RightHand:
                    handBone = HumanBodyBones.RightHand;
                    forearmBone = HumanBodyBones.RightLowerArm;
                    break;
                case HandSlot.LeftHand:
                    handBone = HumanBodyBones.LeftHand;
                    forearmBone = HumanBodyBones.LeftLowerArm;
                    break;
                default:
                    return;
            }

            var hand = _animator.GetBoneTransform(handBone);
            var forearm = _animator.GetBoneTransform(forearmBone);
            if (hand == null || forearm == null) return;

            var axis = hand.position - forearm.position;
            if (axis.sqrMagnitude < 1e-8f) return;

            // Where the prop belongs, measured from the clip's own hand pose.
            var bowWorld = hand.rotation * authoredRotation;
            var bowPosition = weapon.transform.position;

            hand.rotation = Quaternion.AngleAxis(degrees, axis.normalized) * hand.rotation;

            weapon.transform.rotation = bowWorld;
            weapon.transform.position = bowPosition;
        }

        /// <summary>
        /// Turns the head on top of the sampled clip. Applied AFTER SampleAnimation because the
        /// clip overwrites every bone it touches on each sample; a value written before would be
        /// erased on the next frame.
        ///
        /// This exists because a retargeted Mixamo archer can land with the head facing straight
        /// ahead while the bow points sideways, and no amount of root rotation fixes that — the
        /// head has to be corrected against the bow, not against the model.
        /// </summary>
        private void ApplyHeadTurn()
        {
            if (Mathf.Abs(_headTurn) < 0.01f) return;
            if (_animator == null || !_animator.isHuman) return;
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            // World up: a head turn is a yaw, and the bone's own axis is rolled differently on
            // every rig, so local axes would tilt the head instead of turning it.
            head.rotation = Quaternion.AngleAxis(_headTurn, Vector3.up) * head.rotation;
        }

        // ── Retarget ─────────────────────────────────────────────────────────────

        private void RebuildSources()
        {
            _sources.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Model"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var hasHumanClip = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Any(clip => IsUsableClip(clip) && clip.humanMotion);
                if (!hasHumanClip) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null) _sources.Add(asset);
            }

            _sources.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _sourceLabels = _sources.Select(asset => asset.name).ToArray();

            if (_sources.Count == 0)
            {
                SetStatus("Humanoid-источники не найдены.", MessageType.Warning);
                return;
            }

            // Default to the warrior attack source when it is present.
            var preferred = _sources.FindIndex(asset => asset.name.IndexOf("1HAttack", StringComparison.OrdinalIgnoreCase) >= 0);
            _sourceIndex = preferred >= 0 ? preferred : 0;
            RebuildSourceClips();
        }

        private void RebuildSourceClips()
        {
            if (_sourceIndex < 0 || _sourceIndex >= _sources.Count) return;
            var source = _sources[_sourceIndex];
            var path = AssetDatabase.GetAssetPath(source);

            var clips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => IsUsableClip(clip) && clip.humanMotion)
                .ToList();

            _retargetClip = clips.FirstOrDefault();
            SetStatus("Источник: " + source.name + " · клипов " + clips.Count, MessageType.None);

            if (_retargetClip != null) ApplyRetarget(_retargetClip);
        }

        private void ApplyRetarget(AnimationClip clip)
        {
            if (clip == null || _instance == null) return;

            if (!_modelIsHumanoid)
            {
                SetStatus("Модель не Humanoid — ретаргет невозможен.", MessageType.Error);
                return;
            }

            _activeClip = clip;
            _clipIndex = -1;
            _time = 0f;
            _playing = true;

            if (!_clips.Contains(clip)) _clips.Insert(0, clip);
            _clipLabels = _clips.Select((item, i) => (i + 1) + "  " + item.name).ToArray();
            _clipIndex = _clips.IndexOf(clip);

            RebindAnimator();
            Sample();
            SetStatus("Ретаргет: " + clip.name + " → " + _modelAsset.name, MessageType.Info);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private int IndexOfClip(string name) => _clips.FindIndex(clip => clip.name == name);

        private int FindClipContaining(string token) =>
            _clips.FindIndex(clip => clip.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        /// <summary>Shared helper meshes/materials for the viewport overlay and ground grid.</summary>
        private static class MeshOf
        {
            private static Mesh _octahedron;
            private static Mesh _grid;

            public static Mesh Octahedron
            {
                get
                {
                    if (_octahedron != null) return _octahedron;
                    var vertices = new[]
                    {
                        new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f),
                        new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
                        new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
                    };
                    var triangles = new[]
                    {
                        0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2,
                        1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5,
                    };
                    _octahedron = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    _octahedron.vertices = vertices;
                    _octahedron.triangles = triangles;
                    _octahedron.RecalculateNormals();
                    return _octahedron;
                }
            }

            public static Mesh Line(Vector3 from, Vector3 to, float thickness)
            {
                var direction = to - from;
                var length = direction.magnitude;
                if (length < 0.0001f) return Octahedron;

                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                var rotation = Quaternion.FromToRotation(Vector3.up, direction / length);
                var corners = new[]
                {
                    new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
                };
                var vertices = new Vector3[8];
                for (var i = 0; i < 4; i++)
                {
                    vertices[i] = from + rotation * (corners[i] * thickness);
                    vertices[i + 4] = vertices[i] + direction;
                }

                var triangles = new[]
                {
                    0, 1, 2, 0, 2, 3,
                    4, 6, 5, 4, 7, 6,
                    0, 4, 5, 0, 5, 1,
                    1, 5, 6, 1, 6, 2,
                    2, 6, 7, 2, 7, 3,
                    3, 7, 4, 3, 4, 0,
                };
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                return mesh;
            }

            /// <summary>Horizontal line grid centred on the origin; moved by the caller's matrix.</summary>
            public static Mesh Grid(float size)
            {
                if (_grid != null) return _grid;
                var half = size * 0.5f;
                var steps = Mathf.Clamp(Mathf.RoundToInt(size * 4f), 4, 64);
                var step = size / steps;

                var vertices = new List<Vector3>();
                var indices = new List<int>();
                for (var i = 0; i <= steps; i++)
                {
                    var offset = -half + i * step;
                    indices.Add(vertices.Count);
                    vertices.Add(new Vector3(-half, 0f, offset));
                    indices.Add(vertices.Count);
                    vertices.Add(new Vector3(half, 0f, offset));

                    indices.Add(vertices.Count);
                    vertices.Add(new Vector3(offset, 0f, -half));
                    indices.Add(vertices.Count);
                    vertices.Add(new Vector3(offset, 0f, half));
                }

                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                mesh.SetVertices(vertices);
                mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
                mesh.RecalculateBounds();
                _grid = mesh;
                return _grid;
            }
        }

        private static class MaterialOf
        {
            /// <summary>Creates a fresh unlit material; callers own and destroy it.</summary>
            public static Material Unlit(Color color)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                material.color = color;
                return material;
            }
        }
    }
}
