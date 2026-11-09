using System.Collections.Generic;
using Client.Network;
using Simulation.Core;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Представление мира в Unity (M6): держит буфер снапшотов, интерполирует позиции
    /// между двумя последними снапшотами и двигает капсулы. Никакой логики движения здесь нет:
    /// позиция всегда приходит из снапшота (ADR-001, AI-Failure Mode M6).
    /// </summary>
    public sealed class UnityView : IView
    {
        private const float TickRate = 20f;
        private const float LagTicks = 2f;
        private const float MaxCatchUpTicks = 6f;
        private const float CapsuleHalfHeight = 1f;

        private readonly SnapshotBuffer _buffer = new SnapshotBuffer(8);
        private readonly Transform _root;
        private readonly Dictionary<uint, Transform> _entities = new Dictionary<uint, Transform>();
        private readonly Material _playerMaterial;
        private readonly Material _otherMaterial;
        private readonly Material _deadMaterial;
        private float _renderTicks = -1f;

        /// <summary>Создаёт представление; объекты сущностей создаются под <paramref name="root"/>.</summary>
        /// <param name="root">Родительский объект для капсул.</param>
        public UnityView(Transform root)
        {
            _root = root;
            _playerMaterial = CreateMaterial(new Color(0.25f, 0.9f, 0.35f));
            _otherMaterial = CreateMaterial(new Color(0.35f, 0.55f, 0.95f));
            _deadMaterial = CreateMaterial(new Color(0.25f, 0.25f, 0.25f));
        }

        /// <summary>Идентификатор сущности игрока (для камеры и цвета).</summary>
        public uint PlayerEntityId { get; set; }

        /// <summary>Трансформ игрока (появляется после первого снапшота).</summary>
        public Transform Player { get; private set; }

        /// <summary>Сколько снапшотов принято (диагностика для HUD).</summary>
        public int SnapshotsApplied { get; private set; }

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
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "Entity " + entity.Id;
                capsule.transform.SetParent(_root, false);
                capsule.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                transform = capsule.transform;
                _entities[entity.Id] = transform;
            }

            var renderer = transform.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = entity.IsDead
                    ? _deadMaterial
                    : (entity.Id == PlayerEntityId ? _playerMaterial : _otherMaterial);
            }

            var height = entity.IsDead ? CapsuleHalfHeight * 0.3f : CapsuleHalfHeight;
            transform.position = new Vector3(entity.Position.X, entity.Position.Y + height, entity.Position.Z);
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader)
            {
                color = color,
            };

            return material;
        }
    }
}
