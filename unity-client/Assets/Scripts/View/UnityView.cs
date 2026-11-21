using System.Collections.Generic;
using Client.Network;
using Shared;
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

        // Frames an entity visual may be absent from the sampled snapshots before it is destroyed.
        // Half a second at 60 fps: long enough to survive interpolation gaps while a body is being
        // created, short enough that a despawned player is not left standing in the world.
        private const int MissingEntityGraceFrames = 30;

        // Fastest legitimate ground speed in this game. A per-frame displacement implying more than
        // this is a resumed frame (spawn, respawn, tab focus) or a correction, not a stride; the log
        // caught Idle being handed 29,44 m/s from such a frame.
        private const float MaxSpeed = 12f;

        // A simulated speed above this counts as "really running" and refreshes the remembered run
        // direction. Below it the character is stopping, and the direction must not be refreshed.
        private const float MinDirectionSpeed = 0.5f;


        private readonly SnapshotBuffer _buffer = new SnapshotBuffer(8);
        private readonly Transform _root;
        private sealed class EntityVisual
        {
            public Transform Root;
            public WarriorVisual Visual;
            public FantasyVisuals MobVisual;
            public CharacterVisuals Character;
            public WeaponVisuals Weapons;
            public HealthBar HealthBar;
            public bool IsMob;
            public Vector3 LastPosition;
            public bool HasPosition;
            public bool WasDead;
            public bool UsedPrediction;
            public int LastHp;
            /// <summary>
            /// Last Render call in which this entity appeared in the sampled snapshot. Used to
            /// retire visuals whose entity left the world (despawn, disconnect, respawn with a new
            /// id); nothing else removed them, so they stayed in the scene forever.
            /// </summary>
            public int LastSeenFrame;
        }

        private readonly Dictionary<uint, EntityVisual> _entities = new Dictionary<uint, EntityVisual>();
        private readonly Dictionary<uint, GameObject> _corpses = new Dictionary<uint, GameObject>();
        private readonly CharacterVisualSettings _visualSettings;
        private readonly MobVisualSettings _mobVisualSettings;
        private readonly ClientContent? _content;
        private readonly DamageNumbers _damageNumbers;
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
        // Last direction the SIMULATION moved the local player in. The drawn position must not be
        // used for facing (see ApplyEntity), so the direction is remembered here and re-applied
        // while the visible speed decays to zero.
        private Vector3 _lastRunDirection;
        private bool _hasRunDirection;
        // Death/respawn bookkeeping for the input reset: the view is the only place that sees the
        // authoritative IsDead flag, so the edge is derived here and consumed once by the caller.
        private bool _playerKnownDead;
        private bool _hasPrediction;
        /// <summary>Incremented once per Render; entity visuals older than the current frame are retired.</summary>
        private int _frame;

        /// <summary>Создаёт представление; объекты сущностей создаются под <paramref name="root"/>.</summary>
        /// <param name="root">Родительский объект для сетевых корней персонажей.</param>
        /// <param name="content">Контент клиента: по нему мобы отличаются от игроков (ADR-020).</param>
        public UnityView(Transform root, ClientContent? content = null)
        {
            _root = root;
            _content = content;
            _visualSettings = CharacterVisualSettings.Load();
            _mobVisualSettings = MobVisualSettings.Load();
            _damageNumbers = DamageNumbers.Attach(root);
        }

        /// <summary>Идентификатор сущности игрока (для камеры и цвета).</summary>
        public uint PlayerEntityId { get; set; }

        /// <summary>Трансформ игрока (появляется после первого снапшота).</summary>
        public Transform Player { get; private set; }

        /// <summary>
        /// Facing of the local character in world space. The minimap uses it for the player
        /// arrow; it is presentation-only and never changes the network root.
        /// </summary>
        public Vector3 PlayerForward
        {
            get
            {
                if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var state))
                {
                    var facing = state.Character != null ? state.Character.Facing : state.Root;
                    if (facing != null)
                    {
                        var forward = facing.forward;
                        forward.y = 0f;
                        if (forward.sqrMagnitude > 0.0001f)
                        {
                            return forward.normalized;
                        }
                    }
                }

                return Vector3.forward;
            }
        }

        /// <summary>Сколько снапшотов принято (диагностика для HUD).</summary>
        public int SnapshotsApplied { get; private set; }

        /// <summary>Сколько визуальных сущностей создано (игроки и мобы) — для smoke-тестов.</summary>
        public int VisualEntityCount => _entities.Count;

        /// <summary>Сколько визуальных трупов создано — для smoke-тестов.</summary>
        public int CorpseVisualCount => _corpses.Count;

        /// <summary>Character adapter for a snapshot entity, or null for the legacy warrior.</summary>
        public CharacterVisuals? GetCharacterVisual(uint entityId)
        {
            return _entities.TryGetValue(entityId, out var state) ? state.Character : null;
        }

        /// <summary>
        /// World position of a visible entity, or null when it is not rendered here. Used by the
        /// input source to decide whether the player must run closer before striking.
        /// </summary>
        public Vector3? GetEntityPosition(uint entityId)
        {
            if (entityId == 0 || !_entities.TryGetValue(entityId, out var state)) return null;
            if (state.Root == null || !state.Root.gameObject.activeSelf) return null;
            return state.Root.position;
        }

        /// <summary>Последний применённый снапшот — для smoke-тестов.</summary>
        public SimulationSnapshot? LatestSnapshot => _latestSnapshot;

        /// <summary>Diagnostics for the last mob-attack presentation trigger.</summary>
        public string LastMobAttackDebug { get; private set; } = string.Empty;

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

            RetireMissingEntities();
            ApplyCorpses();

            if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var player))
            {
                Player = player.Root;
            }
        }

        /// <summary>
        /// Destroys visuals for entities that stopped appearing in the sampled snapshots.
        ///
        /// Nothing used to remove them: _entities only ever grew, so every respawn (which gives the
        /// player a new entity id) or disconnect left the previous body standing in the scene
        /// forever, frozen in the first frame of its last clip. Two warriors were then drawn on top
        /// of each other — one animating, one a statue — which read in game as a body hovering with
        /// tucked legs that flickered and slid without moving its feet.
        ///
        /// The grace period is generous on purpose: interpolated sampling can omit an entity for a
        /// frame or two while it is being created, and deleting then would make it blink.
        /// </summary>
        private void RetireMissingEntities()
        {
            if (_entities.Count == 0) return;
            _frame++;

            List<uint> stale = null;
            foreach (var pair in _entities)
            {
                if (_frame - pair.Value.LastSeenFrame <= MissingEntityGraceFrames) continue;
                // Never retire the local player: a dropped snapshot while the session is still
                // alive must not delete the body the user is controlling.
                if (pair.Key == PlayerEntityId) continue;
                stale ??= new List<uint>();
                stale.Add(pair.Key);
            }

            if (stale == null) return;
            foreach (var id in stale)
            {
                if (_entities[id]?.Root != null)
                {
                    Object.Destroy(_entities[id].Root.gameObject);
                }

                _entities.Remove(id);
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
            return FantasyVisuals.CreateCorpse(_root, corpseId);
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
                    MobVisual = isMob ? root.GetComponent<FantasyVisuals>() : null,
                    Character = root.GetComponentInChildren<CharacterVisuals>(true),
                    Weapons = root.GetComponentInChildren<WeaponVisuals>(true),
                    HealthBar = root.GetComponentInChildren<HealthBar>(),
                };
                _entities[entity.Id] = state;
            }

            // Marks the entity as present this frame, so RetireMissingEntities leaves it alone.
            state.LastSeenFrame = _frame;

            // Труп моба уже нарисован отдельным объектом: мёртвое тело моба скрываем до респавна.
            if (state.IsMob && state.Root.gameObject.activeSelf == entity.IsDead)
            {
                state.Root.gameObject.SetActive(!entity.IsDead);
            }

            // Death is presentation only: keep the body visible so the one-shot Death state can
            // play and hold its final pose until respawn resets the Animator.
            var hpDelta = state.HasPosition && entity.Hp < state.LastHp ? state.LastHp - entity.Hp : 0;
            var hit = hpDelta > 0 && !entity.IsDead;

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
            // How far the drawn root actually moved since the previous frame. This is the only
            // honest source for the animation: the legs must match the distance the body covers.
            var travelled = position - state.LastPosition;
            travelled.y = 0f;
            var hasTravelled = state.HasPosition && float.IsFinite(travelled.magnitude);
            if (usePrediction)
            {
                // First predicted movement is immediate, including the switch away from snapshots.
                //
                // NOT _predictionVelocity for the MAGNITUDE. The pipeline samples it once per 20 Hz
                // tick and zeroes it on any tick without a freshly read MoveCommand - which happens
                // between ticks, whenever the click-to-move destination is reached, and for the whole
                // length of a one-shot attack. Applying that stale value here (this runs at frame
                // rate) made the character alternate Idle/Locomotion twenty times a second and hand
                // a frozen speed of 4,2 to an Idle pose: "runs in place", welded feet, flickering
                // weapon. The drawn position advances every frame, so its per-frame delta is smooth
                // and stays zero exactly when the character is really standing still.
                var measured = hasTravelled && deltaTime > 0f ? travelled / deltaTime : Vector3.zero;
                // A resumed frame (first after spawn, respawn, tab focus) can carry a huge position
                // delta for a tiny deltaTime; the log showed Idle receiving 29,44 m/s that way. A
                // value above MaxSpeed cannot be a stride, so fall back to zero instead of feeding
                // the animator a number that makes the legs kick.
                velocity = measured.sqrMagnitude > MaxSpeed * MaxSpeed ? Vector3.zero : measured;

                // Direction comes from the simulation, never from the drawn delta. When the player
                // stops, LocalPredictionSmoother zeroes the velocity but keeps decaying its visual
                // correction offset, so the drawn body slides BACKWARDS for 4-12 frames at up to
                // 1,6 m/s. Measuring the facing from that delta turned the warrior around 180
                // degrees exactly when he reached the clicked point - "arrives and turns back the
                // other way, every time" - and produced the "tiny step in the middle of a run"
                // twitches. The remembered run direction is used while the magnitude decays, so the
                // body keeps facing where it last ran and simply slows down.
                var predicted = new Vector3(_predictionVelocity.x, 0f, _predictionVelocity.z);
                if (predicted.sqrMagnitude > MinDirectionSpeed * MinDirectionSpeed)
                {
                    _lastRunDirection = predicted.normalized;
                    _hasRunDirection = true;
                }

                if (_hasRunDirection)
                {
                    velocity = _lastRunDirection * velocity.magnitude;
                }
            }
            else
            {
                reset |= !state.HasPosition || state.UsedPrediction || renderCatchUp
                    || !float.IsFinite(deltaTime) || deltaTime <= 0f
                    || !hasTravelled;
                if (!reset)
                {
                    velocity = travelled / deltaTime;
                    reset = !IsFinite(velocity) || velocity.sqrMagnitude > AnimationDiscontinuitySpeed * AnimationDiscontinuitySpeed;
                }
            }

            state.Visual?.SetMotion(velocity, entity.IsDead, reset);
            state.MobVisual?.SetMotion(velocity, entity.IsDead, reset, deltaTime);
            state.Character?.SetMotion(velocity, entity.IsDead, reset, deltaTime);
            if (hit)
            {
                state.Visual?.PlayHit();
                state.Character?.PlayHit();
            }
            ApplyWeapon(state, entity);
            if (hpDelta > 0 && entity.Id == PlayerEntityId)
            {
                // The server snapshot carries HP but no mob-attack event. The authoritative
                // damage on the local player is the signal that a nearby mob just swung;
                // without this mobs dealt damage while standing in Idle.
                PlayMobAttackNear(position);
            }
            if (hpDelta > 0)
            {
                // Snapshot HP is authoritative: the same delta the server applied is the number
                // the player sees, including lethal damage and damage taken from mobs.
                _damageNumbers.Show(position + (Vector3.up * (state.IsMob ? 2.25f : 2.0f)), hpDelta);
            }

            state.HealthBar?.SetHp(entity.Hp, entity.MaxHp);
            state.HealthBar?.SetResource(entity.Resource, entity.MaxResource);
            // Preserve authored skin/metal/leather/cloth accents; equipment is already in HUD.
            state.LastPosition = position;
            state.HasPosition = IsFinite(position);
            state.WasDead = entity.IsDead;
            state.UsedPrediction = usePrediction;
            state.LastHp = entity.Hp;
        }

        /// <summary>
        /// Local ability feedback: turns the player to the target and plays the swing or shot.
        /// The projectile is spawned here (not from the server event) so the shot leaves the
        /// bow/staff on the same frame as the animation, without waiting a full round trip.
        /// </summary>
        public void TriggerAttack(uint targetEntityId)
        {
            if (PlayerEntityId == 0 || !_entities.TryGetValue(PlayerEntityId, out var state))
            {
                return;
            }

            var targetPosition = FaceAttackTarget(state, targetEntityId);
            state.Visual?.PlayAttack();
            state.Character?.PlayAttack();
            var kind = state.Character != null ? state.Character.Equipped : WeaponKind.Sword;
            if (kind == WeaponKind.Bow || kind == WeaponKind.Staff)
            {
                SpawnProjectile(state, targetPosition);
            }
            else
            {
                // Ближний бой: дуга читается лучше, если подчеркнуть её вспышкой у клинка,
                // а не только позой персонажа.
                var origin = AttackOrigin(state);
                var forward = state.Character != null ? state.Character.Facing.forward : state.Root.forward;
                CombatVfx.AttackFlash(origin + (forward * 0.55f), Quaternion.LookRotation(forward, Vector3.up),
                    new Color(1f, 0.93f, 0.7f));
            }
        }

        /// <summary>Remote attack from <see cref="MessageType.CombatEvent"/> (Protocol v11).</summary>
        public void PlayCombatEvent(CombatEvent evt)
        {
            if (evt.AttackerEntityId == PlayerEntityId || evt.AttackerEntityId == 0) return;
            if (!_entities.TryGetValue(evt.AttackerEntityId, out var attacker) || attacker.Root == null) return;
            if (!attacker.Root.gameObject.activeSelf) return;

            var targetPosition = FaceAttackTarget(attacker, evt.TargetEntityId);
            attacker.Character?.PlayAttack();
            attacker.Visual?.PlayAttack();

            if (evt.Style == CombatStyle.Arrow || evt.Style == CombatStyle.Magic)
            {
                SpawnProjectile(attacker, targetPosition);
            }
            else
            {
                // Melee feedback: a short flash at the blade so a remote swing reads even at
                // the edge of the screen where the clip itself is small.
                var origin = AttackOrigin(attacker);
                var forward = attacker.Character != null ? attacker.Character.Facing.forward : attacker.Root.forward;
                CombatVfx.AttackFlash(origin + (forward * 0.55f), Quaternion.LookRotation(forward, Vector3.up),
                    new Color(1f, 0.9f, 0.6f));
            }
        }

        /// <summary>Rotates the visual toward the attack target; returns the world aim point.</summary>
        private Vector3 FaceAttackTarget(EntityVisual attacker, uint targetEntityId)
        {
            var origin = attacker.Root != null ? attacker.Root.position : Vector3.zero;
            var aim = origin + (attacker.Root != null ? attacker.Root.forward : Vector3.forward) * 4f;

            if (targetEntityId != 0 && _entities.TryGetValue(targetEntityId, out var target) && target.Root != null)
            {
                aim = target.Root.position + (Vector3.up * 1.05f);
            }
            else
            {
                // Без цели стреляем/бьём туда, куда повёрнут персонаж.
                var forward = attacker.Character != null ? attacker.Character.Facing.forward : attacker.Root.forward;
                aim = origin + (Vector3.up * 1.05f) + (forward * 6f);
            }

            var direction = aim - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0004f && attacker.Character != null)
            {
                // TurnTo, not Face: an instant rotation is overwritten by velocity facing on the
                // next SetMotion, so the model was never actually aimed when the swing began.
                // A bow needs the left shoulder toward the target, so the arrow clears the body.
                var offset = attacker.Character.Equipped == WeaponKind.Bow
                    ? attacker.Character.BowYawOffsetDeg
                    : 0f;
                attacker.Character.TurnTo(direction.normalized, offset);
            }

            return aim;
        }

        /// <summary>
        /// World position a projectile leaves from. The bow hangs in the left hand and points
        /// sideways from the body, so "weapon forward" sends the arrow across the chest — the
        /// flight line must be built from the shooter toward the target instead.
        /// </summary>
        private static Vector3 AttackOrigin(EntityVisual state)
        {
            var weapon = state.Character != null ? state.Character.EquippedTransform : null;
            if (weapon != null)
            {
                return weapon.position + (Vector3.up * 0.1f);
            }

            return state.Root != null ? state.Root.position + (Vector3.up * 1.2f) : Vector3.zero;
        }

        /// <summary>
        /// Launch point on the bow string, pushed along the real flight line so the arrow starts
        /// in front of the shooter rather than inside the body.
        /// </summary>
        private static Vector3 ProjectileOrigin(EntityVisual state, Vector3 targetPosition)
        {
            var anchor = AttackOrigin(state);
            var direction = targetPosition - anchor;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0004f)
            {
                direction = state.Root != null ? state.Root.forward : Vector3.forward;
            }

            return anchor + (direction.normalized * 0.35f);
        }

        private static void SpawnProjectile(EntityVisual state, Vector3 targetPosition)
        {
            var kind = state.Character != null ? state.Character.Equipped : WeaponKind.Sword;
            var origin = ProjectileOrigin(state, targetPosition);
            switch (kind)
            {
                case WeaponKind.Bow:
                    ProjectileVfx.Arrow(origin, targetPosition, 0.08f);
                    break;
                case WeaponKind.Staff:
                    ProjectileVfx.Orb(origin, targetPosition, 0.12f);
                    break;
            }
        }

        /// <summary>Small shrinking circle at a clicked world point.</summary>
        public void ShowClickMarker(Vector3 position)
        {
            CombatVfx.ClickMarker(position);
        }

        /// <summary>Gather feedback for the local player; the server still validates the node.</summary>
        public void TriggerGather(Vector3 position)
        {
            CombatVfx.GatherBurst(position, new Color(0.6f, 0.9f, 0.45f));
        }

        /// <summary>Shows a local damage flash; used by regression checks and future hit reactions.</summary>
        public void PlayDamage(uint entityId)
        {
            if (entityId != 0 && _entities.TryGetValue(entityId, out var state))
            {
                state.Character?.PlayHit();
                state.Visual?.PlayHit();
            }
        }

        /// <summary>
        /// Switches the local player's visible weapon (1 = sword, 2 = axe). Client-only: the
        /// server ability, damage and range are unchanged in M10.11.
        /// </summary>
        public void EquipWeapon(WeaponKind kind)
        {
            if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var state))
            {
                state.Weapons?.Equip(kind);
                state.Character?.Equip(kind);
            }
        }

        /// <summary>
        /// Starts the one-shot attack animation on the closest mob when the local player takes
        /// damage. Mob attacks are server-side; the current protocol has no explicit attack event.
        /// </summary>
        private void PlayMobAttackNear(Vector3 playerPosition)
        {
            // Snapshot v8 has no attacker id. Play the swing on every mob that is close enough
            // to have dealt the damage; picking only the closest could animate another mob and
            // leave the actual attacker in Idle (observed in the 30 s bot run).
            var candidates = 0;
            var played = 0;
            foreach (var pair in _entities)
            {
                var candidate = pair.Value;
                if (!candidate.IsMob || candidate.Character == null || candidate.Root == null
                    || !candidate.Root.gameObject.activeSelf)
                {
                    continue;
                }

                candidates++;
                if (Vector3.Distance(candidate.Root.position, playerPosition) <= 3.5f)
                {
                    candidate.Character.PlayAttack();
                    played++;
                }
            }

            LastMobAttackDebug = "candidates=" + candidates + " played=" + played
                + " player=" + playerPosition.ToString("F2");
        }

        private void ApplyWeapon(EntityVisual state, InterpolatedEntity entity)
        {
            if (state.IsMob || entity.WeaponItemId == 0 || _content == null)
            {
                return;
            }

            if (_content.Config.Items.TryGetValue(entity.WeaponItemId, out var item))
            {
                state.Character?.EquipFromArchetype(item.Archetype);
                state.Weapons?.EquipFromArchetype(item.Archetype);
            }
        }

        /// <summary>
        /// Returns true exactly once on the frame the local player goes from dead to alive.
        ///
        /// The caller uses this edge to drop held movement input. A player who dies while holding
        /// the right mouse button keeps a click-to-move destination recorded against the OLD
        /// position; after respawn the next tick sends a MoveCommand towards it, so the character
        /// runs off on its own until the player presses something. Exposing the edge here keeps the
        /// decision data-driven: the view is the only place that sees the authoritative dead/alive
        /// flag, so the input layer must not try to infer it.
        /// </summary>
        public bool ConsumePlayerRespawnEdge()
        {
            if (PlayerEntityId == 0 || !_entities.ContainsKey(PlayerEntityId))
            {
                _playerKnownDead = false;
                return false;
            }

            var alive = _latestSnapshot != null && !IsPlayerDeadInLatestSnapshot();
            var edged = alive && _playerKnownDead;
            _playerKnownDead = !alive;
            return edged;
        }

        private bool IsPlayerDeadInLatestSnapshot()
        {
            if (_latestSnapshot == null) return false;
            for (var i = 0; i < _latestSnapshot.Entities.Count; i++)
            {
                if (_latestSnapshot.Entities[i].Id == PlayerEntityId) return _latestSnapshot.Entities[i].IsDead;
            }

            return false;
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
                    && ((state.Visual != null && state.Visual.IsAttackPlaying)
                        || (state.Character != null && state.Character.IsAttackPlaying));
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private Transform CreateCharacter(uint entityId) => _visualSettings.Create(_root, entityId);

        private Transform CreateMob(uint actorId)
        {
            var mob = _content?.FindMobByActorId(actorId);

            // The visual is looked up by the mob's own key, not by its zone. A zone key gave one
            // look per zone, so a second mob in the same zone silently wore the first one's body.
            // The zone is still passed to the primitive fallback, which colours by zone.
            var imported = _mobVisualSettings?.Create(_root, actorId, mob?.Key);
            if (imported != null) return imported;
            var zone = mob?.ZoneKey;
            var root = FantasyVisuals.CreateMob(_root, actorId, zone);
            HealthBar.Attach(root, zone == "red" ? 2.95f : zone == "yellow" ? 2.05f : 1.8f);
            return root;
        }

    }
}
