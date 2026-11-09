using System.Collections.Generic;
using Client.Network;
using Simulation.Core;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Представление мира в Unity (M6, M7): держит буфер снапшотов, интерполирует позиции
    /// между двумя последними снапшотами и двигает капсулы. Своей логики движения здесь нет:
    /// чужие сущности идут из снапшотов, игрок — из предсказания клиента (M7), которое
    /// сглаживается, чтобы коррекции реконсиляции не выглядели телепортом.
    /// </summary>
    public sealed class UnityView : IPredictedView
    {
        private const float TickRate = 20f;
        private const float LagTicks = 2f;
        private const float MaxCatchUpTicks = 6f;
        private const float CapsuleHalfHeight = 1f;

        private readonly SnapshotBuffer _buffer = new SnapshotBuffer(8);
        private readonly Transform _root;
        private readonly Dictionary<uint, Transform> _entities = new Dictionary<uint, Transform>();
        private readonly Material _playerBody;
        private readonly Material _playerSkin;
        private readonly Material _playerAccent;
        private readonly Material _otherBody;
        private readonly Material _otherSkin;
        private readonly Material _otherAccent;
        private readonly Material _deadMaterial;
        // Локальный игрок рисуется без задержки буфера (нулевая задержка управления): позиция
        // берётся из последнего предсказания, между тиками экстраполируется, а коррекции
        // реконсиляции гасятся визуальным оффсетом. Удалённые игроки идут через буфер
        // снапшотов с задержкой LagTicks = 2 (100 мс) — им важна плавность, а не отзывчивость.
        private readonly LocalPredictionSmoother _localPrediction = new LocalPredictionSmoother();
        private float _renderTicks = -1f;
        private Vector3 _predictedPosition;
        private Vector3 _displayPosition;
        private bool _hasPrediction;

        /// <summary>Создаёт представление; объекты сущностей создаются под <paramref name="root"/>.</summary>
        /// <param name="root">Родительский объект для капсул.</param>
        public UnityView(Transform root)
        {
            _root = root;
            // Стилизованная палитра (M10): герой — зелёно-золотой, чужие — синие, мёртвые — серые.
            _playerBody = CreateToonMaterial(new Color(0.22f, 0.62f, 0.36f), new Color(0.08f, 0.20f, 0.16f), new Color(0.95f, 0.92f, 0.70f));
            _playerSkin = CreateToonMaterial(new Color(0.96f, 0.80f, 0.62f), new Color(0.42f, 0.26f, 0.20f), new Color(1f, 0.95f, 0.85f));
            _playerAccent = CreateToonMaterial(new Color(0.95f, 0.75f, 0.25f), new Color(0.35f, 0.22f, 0.05f), new Color(1f, 0.95f, 0.75f));
            _otherBody = CreateToonMaterial(new Color(0.26f, 0.42f, 0.78f), new Color(0.08f, 0.12f, 0.26f), new Color(0.75f, 0.85f, 1f));
            _otherSkin = CreateToonMaterial(new Color(0.90f, 0.74f, 0.60f), new Color(0.36f, 0.24f, 0.20f), new Color(1f, 0.95f, 0.85f));
            _otherAccent = CreateToonMaterial(new Color(0.60f, 0.66f, 0.72f), new Color(0.18f, 0.20f, 0.24f), new Color(0.9f, 0.95f, 1f));
            _deadMaterial = CreateToonMaterial(new Color(0.35f, 0.35f, 0.38f), new Color(0.12f, 0.12f, 0.14f), new Color(0.6f, 0.6f, 0.6f));
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
            _hasPrediction = true;
            LastReconciliationCorrected = reconciled;
        }

        /// <inheritdoc />
        public void ApplySnapshot(SimulationSnapshot snapshot)
        {
            _buffer.Add(snapshot);
            SnapshotsApplied++;

            if (_renderTicks < 0f)
            {
                _renderTicks = snapshot.Tick;
            }
        }

        /// <inheritdoc />
        public void Render(float frameDeltaTime)
        {
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
            if (target - _renderTicks > MaxCatchUpTicks)
            {
                _renderTicks = target;
            }

            _renderTicks = Mathf.Min(_renderTicks + (frameDeltaTime * TickRate), target);
            if (_renderTicks < _buffer.EarliestTick)
            {
                _renderTicks = _buffer.EarliestTick;
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
                ApplyEntity(entity);
            }

            if (PlayerEntityId != 0 && _entities.TryGetValue(PlayerEntityId, out var player))
            {
                Player = player;
            }
        }

        private void ApplyEntity(InterpolatedEntity entity)
        {
            if (!_entities.TryGetValue(entity.Id, out var transform))
            {
                transform = CreateCharacter(entity.Id);
                _entities[entity.Id] = transform;
            }

            var renderers = transform.GetComponentsInChildren<Renderer>();
            if (entity.IsDead)
            {
                foreach (var renderer in renderers)
                {
                    renderer.sharedMaterial = _deadMaterial;
                }

                transform.localScale = new Vector3(0.8f, 0.35f, 0.8f);
            }
            else
            {
                var isPlayer = entity.Id == PlayerEntityId;
                foreach (var renderer in renderers)
                {
                    renderer.sharedMaterial = ResolveMaterial(renderer.name, isPlayer);
                }

                transform.localScale = Vector3.one;
            }

            var usePrediction = _hasPrediction && entity.Id == PlayerEntityId;

            transform.position = usePrediction
                ? new Vector3(_displayPosition.x, _displayPosition.y, _displayPosition.z)
                : new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
        }

        /// <summary>
        /// Собирает стилизованного персонажа из простых форм (M10): тело, голова, наплечники, оружие.
        /// Никаких внешних ассетов: силуэт читается, шейдер даёт toon-освещение и контур.
        /// </summary>
        private Transform CreateCharacter(uint entityId)
        {
            var root = new GameObject("Entity " + entityId);
            root.transform.SetParent(_root, false);

            AddPrimitive(root.transform, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 0.85f, 0.55f));
            AddPrimitive(root.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.82f, 0f), new Vector3(0.46f, 0.46f, 0.46f));
            AddPrimitive(root.transform, PrimitiveType.Cube, "Shoulders", new Vector3(0f, 1.45f, 0f), new Vector3(0.86f, 0.16f, 0.42f));
            var weapon = AddPrimitive(root.transform, PrimitiveType.Cube, "Weapon", new Vector3(0.48f, 1.0f, 0.1f), new Vector3(0.07f, 1.0f, 0.07f));
            weapon.localRotation = Quaternion.Euler(0f, 0f, -18f);

            return root.transform;
        }

        private static Transform AddPrimitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;

            var collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                // Коллайдеры не нужны: движение и попадания считает сервер, физики на клиенте нет.
                UnityEngine.Object.Destroy(collider);
            }

            return primitive.transform;
        }

        private Material ResolveMaterial(string partName, bool isPlayer)
        {
            if (partName == "Head")
            {
                return isPlayer ? _playerSkin : _otherSkin;
            }

            if (partName == "Body")
            {
                return isPlayer ? _playerBody : _otherBody;
            }

            return isPlayer ? _playerAccent : _otherAccent;
        }

        private static Material CreateToonMaterial(Color baseColor, Color shadeColor, Color rimColor)
        {
            var shader = Shader.Find("Albion/Toon Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                return new Material(shader) { color = baseColor };
            }

            var material = new Material(shader);
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_ShadeColor", shadeColor);
            material.SetColor("_RimColor", rimColor);
            material.SetFloat("_RimStrength", 0.32f);
            material.SetFloat("_RimPower", 2.6f);
            material.SetFloat("_OutlineWidth", 0.015f);
            material.SetFloat("_ShadowSmoothness", 0.06f);
            return material;
        }
    }
}
