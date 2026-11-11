using System.Collections.Generic;
using Client.Network;
using Simulation.Core;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Представление мира в Unity (M6, M7): держит буфер снапшотов, интерполирует позиции
    /// между двумя последними снапшотами и двигает корни персонажей. Своей логики движения здесь нет:
    /// чужие сущности идут из снапшотов, игрок — из предсказания клиента (M7), которое
    /// сглаживается, чтобы коррекции реконсиляции не выглядели телепортом.
    /// </summary>
    public sealed class UnityView : IPredictedView
    {
        private const float TickRate = 20f;
        private const float LagTicks = 2f;
        private const float MaxCatchUpTicks = 6f;
        // Animation-only discontinuity guard: >2 m per network tick is a correction/teleport,
        // not a stride. This never clamps, changes, or predicts the entity's position.
        private const float AnimationDiscontinuitySpeed = 2f * TickRate;


        private readonly SnapshotBuffer _buffer = new SnapshotBuffer(8);
        private readonly Transform _root;
        private sealed class EntityVisual
        {
            public Transform Root;
            public WarriorVisual Visual;
            public WeaponVisuals Weapons;
            public HealthBar HealthBar;
            public EquipmentTint Tint;
            public bool IsMob;
            public Vector3 LastPosition;
            public bool HasPosition;
            public bool WasDead;
            public bool UsedPrediction;
            public int LastHp;
        }

        private readonly Dictionary<uint, EntityVisual> _entities = new Dictionary<uint, EntityVisual>();
        private readonly Dictionary<uint, GameObject> _corpses = new Dictionary<uint, GameObject>();
        private readonly CharacterVisualSettings _visualSettings;
        private readonly ClientContent? _content;
        private SimulationSnapshot? _latestSnapshot;
        // Локальный игрок рисуется без задержки буфера (нулевая задержка управления): позиция
        // берётся из последнего предсказания, между тиками экстраполируется, а коррекции
        // реконсиляции гасятся визуальным оффсетом. Удалённые игроки идут через буфер
        // снапшотов с задержкой LagTicks = 2 (100 мс) — им важна плавность, а не отзывчивость.
        private readonly LocalPredictionSmoother _localPrediction = new LocalPredictionSmoother();
        private float _renderTicks = -1f;
        private Vector3 _predictedPosition;
        private Vector3 _predictionVelocity;
        private Vector3 _displayPosition;
        private bool _hasPrediction;

        /// <summary>Создаёт представление; объекты сущностей создаются под <paramref name="root"/>.</summary>
        /// <param name="root">Родительский объект для сетевых корней персонажей.</param>
        /// <param name="content">Контент клиента: по нему мобы отличаются от игроков (ADR-020).</param>
        public UnityView(Transform root, ClientContent? content = null)
        {
            _root = root;
            _content = content;
            _visualSettings = CharacterVisualSettings.Load();
        }

        /// <summary>Идентификатор сущности игрока (для камеры и цвета).</summary>
        public uint PlayerEntityId { get; set; }

        /// <summary>Трансформ игрока (появляется после первого снапшота).</summary>
        public Transform Player { get; private set; }

        /// <summary>Сколько снапшотов принято (диагностика для HUD).</summary>
        public int SnapshotsApplied { get; private set; }

        /// <summary>Была ли последняя реконсиляция (диагностика для HUD).</summary>
        public bool LastReconciliationCorrected { get; private set; }

        /// <summary>Предсказанная позиция игрока (диагностика для HUD).</summary>
        public Vector3 PredictedPosition => _predictedPosition;

        /// <inheritdoc />
        public void SetPredictedPlayerPosition(uint tick, System.Numerics.Vector3 position, System.Numerics.Vector3 velocity, bool reconciled)
        {
            _localPrediction.AddSample(position, velocity);
            _predictedPosition = new Vector3(position.X, position.Y, position.Z);
            // Keep simulation velocity separate from the reconciled/smoothed display displacement.
            _predictionVelocity = new Vector3(velocity.X, 0f, velocity.Z);
            if (!IsFinite(_predictionVelocity)) _predictionVelocity = Vector3.zero;
            _hasPrediction = true;
            LastReconciliationCorrected = reconciled;
        }

        /// <inheritdoc />
        public void ApplySnapshot(SimulationSnapshot snapshot)
        {
            _buffer.Add(snapshot);
            _latestSnapshot = snapshot;
            SnapshotsApplied++;

            if (_renderTicks < 0f)
            {
                _renderTicks = snapshot.Tick;
            }
        }

        /// <inheritdoc />
        public void Render(float frameDeltaTime)
        {
            if (!float.IsFinite(frameDeltaTime) || frameDeltaTime < 0f) frameDeltaTime = 0f;
            if (_buffer.Count == 0)
            {
                return;
            }

            var target = _buffer.LatestTick - LagTicks;
            if (target < _buffer.EarliestTick)
            {
                target = _buffer.EarliestTick;
            }

            // Отстали слишком сильно (лаг сервера или пауза) — не «догоняем» в замедленном темпе.
            var renderCatchUp = target - _renderTicks > MaxCatchUpTicks;
            if (renderCatchUp)
            {
                _renderTicks = target;
            }

            _renderTicks = Mathf.Min(_renderTicks + (frameDeltaTime * TickRate), target);
            if (_renderTicks < _buffer.EarliestTick)
            {
                _renderTicks = _buffer.EarliestTick;
                renderCatchUp = true;
            }

            if (_hasPrediction)
            {
                // Без задержки: положение локального игрока сразу следует за предсказанием,
                // между тиками (20 Гц) экстраполируется, расхождения гасятся визуальным оффсетом.
                _localPrediction.Advance(frameDeltaTime);
                var display = _localPrediction.Position;
                _displayPosition = new Vector3(display.X, display.Y, display.Z);
            }

            foreach (var entity in _buffer.Sample(_renderTicks))
            {
                ApplyEntity(entity, frameDeltaTime, renderCatchUp);
            }

            ApplyCorpses();

            if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var player))
            {
                Player = player.Root;
            }
        }

        /// <summary>Обновляет визуальные трупы по последнему снапшоту (Protocol v8).</summary>
        private void ApplyCorpses()
        {
            if (_latestSnapshot == null)
            {
                return;
            }

            var alive = new HashSet<uint>();
            foreach (var corpse in _latestSnapshot.Corpses)
            {
                alive.Add(corpse.Id);
                if (!_corpses.TryGetValue(corpse.Id, out var visual) || visual == null)
                {
                    visual = CreateCorpse(corpse.Id);
                    _corpses[corpse.Id] = visual;
                }

                visual.transform.position = new Vector3(corpse.Position.X, corpse.Position.Y, corpse.Position.Z);
            }

            var removed = new List<uint>();
            foreach (var pair in _corpses)
            {
                if (!alive.Contains(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }

            foreach (var id in removed)
            {
                if (_corpses[id] != null)
                {
                    Object.Destroy(_corpses[id]);
                }

                _corpses.Remove(id);
            }
        }

        private GameObject CreateCorpse(uint corpseId)
        {
            var root = new GameObject("Corpse " + corpseId);
            root.transform.SetParent(_root, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Corpse Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.25f, 0.7f);
            var collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = CreateLitMaterial(new Color(0.42f, 0.42f, 0.46f));
            }

            return root;
        }

        private void ApplyEntity(InterpolatedEntity entity, float deltaTime, bool renderCatchUp)
        {
            if (!_entities.TryGetValue(entity.Id, out var state))
            {
                var isMob = _content != null && _content.FindMobByActorId(entity.ActorId) != null;
                var root = isMob ? CreateMob(entity.ActorId) : CreateCharacter(entity.Id);
                state = new EntityVisual
                {
                    Root = root,
                    IsMob = isMob,
                    Visual = isMob ? null : root.GetComponent<WarriorVisual>(),
                    Weapons = isMob ? null : root.GetComponentInChildren<WeaponVisuals>(true),
                    HealthBar = root.GetComponentInChildren<HealthBar>(),
                    Tint = isMob ? null : new EquipmentTint(root, _content),
                };
                _entities[entity.Id] = state;
            }

            // Труп моба уже нарисован отдельным объектом: мёртвое тело моба скрываем до респавна.
            if (state.IsMob && state.Root.gameObject.activeSelf == entity.IsDead)
            {
                state.Root.gameObject.SetActive(!entity.IsDead);
            }

            // Death is presentation only: keep the body visible so the one-shot Death state can
            // play and hold its final pose until respawn resets the Animator.
            var hit = state.HasPosition && entity.Hp < state.LastHp && !entity.IsDead;

            var usePrediction = _hasPrediction && entity.Id == PlayerEntityId;
            // A dead local player must not keep sliding on held movement input while the
            // Death pose is held; the snapshot position is authoritative until respawn.
            if (entity.IsDead) usePrediction = false;
            var position = usePrediction
                ? new Vector3(_displayPosition.x, _displayPosition.y, _displayPosition.z)
                : new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
            state.Root.position = position;

            var respawn = state.WasDead && !entity.IsDead;
            var velocity = Vector3.zero;
            var reset = respawn;
            if (usePrediction)
            {
                // First predicted movement is immediate, including the switch away from snapshots.
                velocity = _predictionVelocity;
            }
            else
            {
                var displacement = position - state.LastPosition;
                displacement.y = 0f;
                reset |= !state.HasPosition || state.UsedPrediction || renderCatchUp
                    || !float.IsFinite(deltaTime) || deltaTime <= 0f
                    || !IsFinite(position) || !IsFinite(displacement);
                if (!reset)
                {
                    velocity = displacement / deltaTime;
                    reset = !IsFinite(velocity) || velocity.sqrMagnitude > AnimationDiscontinuitySpeed * AnimationDiscontinuitySpeed;
                }
            }

            state.Visual?.SetMotion(velocity, entity.IsDead, reset);
            if (hit) state.Visual?.PlayHit();
            state.HealthBar?.SetHp(entity.Hp, entity.MaxHp);
            state.HealthBar?.SetResource(entity.Resource, entity.MaxResource);
            state.Tint?.Apply(_latestSnapshot, entity.Id);
            state.LastPosition = position;
            state.HasPosition = IsFinite(position);
            state.WasDead = entity.IsDead;
            state.UsedPrediction = usePrediction;
            state.LastHp = entity.Hp;
        }

        /// <summary>Local ability feedback: plays one attack swing when the prediction starts it.</summary>
        public void TriggerAttack()
        {
            if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var state))
                state.Visual?.PlayAttack();
        }

        /// <summary>
        /// Switches the local player's visible weapon (1 = sword, 2 = axe). Client-only: the
        /// server ability, damage and range are unchanged in M10.11.
        /// </summary>
        public void EquipWeapon(WeaponKind kind)
        {
            if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var state))
                state.Weapons?.Equip(kind);
        }

        /// <summary>
        /// True while the local warrior's Attack one-shot has not reached its exit phase.
        /// The input layer uses it to avoid sending a new ability command / restarting the clip.
        /// </summary>
        public bool IsPlayerAttackPlaying
        {
            get
            {
                return PlayerEntityId != 0
                    && _entities.TryGetValue(PlayerEntityId, out var state)
                    && state.Visual != null
                    && state.Visual.IsAttackPlaying;
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private Transform CreateCharacter(uint entityId) => _visualSettings.Create(_root, entityId);

        private Transform CreateMob(uint actorId)
        {
            var root = new GameObject("Mob " + actorId).transform;
            root.SetParent(_root, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Mob Body";
            body.transform.SetParent(root, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.85f, 0.9f, 0.85f);
            var collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = CreateLitMaterial(MobColor(actorId));
            }

            HealthBar.Attach(root, 2.1f);
            return root;
        }

        private Color MobColor(uint actorId)
        {
            var zone = _content?.FindMobByActorId(actorId)?.ZoneKey;
            switch (zone)
            {
                case "blue":
                    return new Color(0.35f, 0.55f, 0.95f);
                case "yellow":
                    return new Color(0.95f, 0.75f, 0.25f);
                case "red":
                    return new Color(0.9f, 0.2f, 0.2f);
                default:
                    return new Color(0.85f, 0.25f, 0.25f);
            }
        }

        private static Material CreateLitMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        /// <summary>
        /// Tint игрока по надетому оружию/броне (Protocol v8): цвет ветки оружия, светлее при
        /// надетой броне. Использует MaterialPropertyBlock, чтобы не менять общие материалы модели.
        /// </summary>
        private sealed class EquipmentTint
        {
            private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
            private readonly Renderer[] _renderers;
            private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
            private readonly ClientContent? _content;
            private int _lastSignature;

            public EquipmentTint(Transform root, ClientContent? content)
            {
                _renderers = root.GetComponentsInChildren<Renderer>(true);
                _content = content;
            }

            public void Apply(SimulationSnapshot? snapshot, uint entityId)
            {
                if (snapshot == null)
                {
                    return;
                }

                for (var i = 0; i < snapshot.Entities.Count; i++)
                {
                    var entity = snapshot.Entities[i];
                    if (entity.Id != entityId)
                    {
                        continue;
                    }

                    var signature = (int)(entity.WeaponItemId
                        ^ (entity.ChestItemId << 5)
                        ^ (entity.LegsItemId << 10)
                        ^ (entity.FeetItemId << 15));
                    if (signature == _lastSignature)
                    {
                        return;
                    }

                    _lastSignature = signature;
                    if (entity.WeaponItemId == 0 && entity.ChestItemId == 0 && entity.LegsItemId == 0 && entity.FeetItemId == 0)
                    {
                        foreach (var renderer in _renderers)
                        {
                            renderer.SetPropertyBlock(null);
                        }

                        return;
                    }

                    var color = new Color(0.82f, 0.72f, 0.62f);
                    var archetype = ResolveArchetype(entity.WeaponItemId);
                    switch (archetype)
                    {
                        case "warrior":
                            color = new Color(0.92f, 0.5f, 0.3f);
                            break;
                        case "archer":
                            color = new Color(0.4f, 0.85f, 0.45f);
                            break;
                        case "mage":
                            color = new Color(0.45f, 0.6f, 0.95f);
                            break;
                    }

                    if (entity.ChestItemId != 0)
                    {
                        color = Color.Lerp(color, Color.white, 0.15f);
                    }

                    _block.SetColor(BaseColor, color);
                    foreach (var renderer in _renderers)
                    {
                        renderer.SetPropertyBlock(_block);
                    }

                    return;
                }
            }

            private string? ResolveArchetype(uint weaponItemId)
            {
                if (weaponItemId == 0 || _content == null)
                {
                    return null;
                }

                try
                {
                    return _content.Config.GetItem(weaponItemId).Archetype;
                }
                catch (System.InvalidOperationException)
                {
                    return null;
                }
            }
        }
    }
}
