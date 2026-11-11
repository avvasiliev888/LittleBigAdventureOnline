using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Ветка дерева навыков для одного архетипа оружия (ADR-015). Ветка линейная:
    /// узлы идут по возрастанию порога Славы, первый узел открыт с нуля и даёт базовую способность.
    /// Слава копится отдельно в каждой ветке, поэтому смена оружия не сбрасывает прогресс.
    /// </summary>
    public sealed class SkillArchetype
    {
        private readonly IReadOnlyList<SkillNode> _nodes;

        /// <summary>Создаёт ветку дерева навыков.</summary>
        /// <param name="key">Ключ архетипа (например, <c>warrior</c>); уникален в дереве.</param>
        /// <param name="name">Читаемое имя ветки.</param>
        /// <param name="nodes">Узлы ветки в порядке возрастания порога Славы.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="nodes"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если ключ/имя пусты или узлы нарушают порядок и требования.</exception>
        public SkillArchetype(string key, string name, IReadOnlyList<SkillNode> nodes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ архетипа не может быть пустым.", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Имя архетипа не может быть пустым.", nameof(name));
            }

            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (nodes.Count == 0)
            {
                throw new ArgumentException("Ветка '" + key + "' должна содержать хотя бы один узел.", nameof(nodes));
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == null)
                {
                    throw new ArgumentException("Ветка '" + key + "' содержит null в позиции " + i + ".", nameof(nodes));
                }
            }

            if (nodes[0].FameThreshold != 0)
            {
                throw new ArgumentException(
                    "Первый узел ветки '" + key + "' должен быть открыт с нуля: порог " + nodes[0].FameThreshold + ".",
                    nameof(nodes));
            }

            if (nodes[0].Kind != SkillNodeKind.Active)
            {
                throw new ArgumentException(
                    "Первый узел ветки '" + key + "' должен быть активным: базовая способность архетипа.",
                    nameof(nodes));
            }

            for (var i = 1; i < nodes.Count; i++)
            {
                if (nodes[i].FameThreshold <= nodes[i - 1].FameThreshold)
                {
                    throw new ArgumentException(
                        "Пороги Славы ветки '" + key + "' должны строго возрастать: узел " + nodes[i].Id
                        + " (" + nodes[i].FameThreshold + ") после узла " + nodes[i - 1].Id
                        + " (" + nodes[i - 1].FameThreshold + ").",
                        nameof(nodes));
                }
            }

            Key = key;
            Name = name;
            _nodes = new List<SkillNode>(nodes).AsReadOnly();
        }

        /// <summary>Ключ архетипа.</summary>
        public string Key { get; }

        /// <summary>Читаемое имя ветки.</summary>
        public string Name { get; }

        /// <summary>Узлы ветки в порядке возрастания порога Славы.</summary>
        public IReadOnlyList<SkillNode> Nodes => _nodes;

        /// <summary>
        /// Способность, которая действует при данной Славе в ветке: последний открытый активный узел.
        /// Первый узел всегда активный, поэтому результат положителен при любой Славе.
        /// </summary>
        /// <param name="fame">Накопленная Слава в этой ветке.</param>
        /// <returns>Идентификатор активной способности.</returns>
        public uint ResolveAbilityId(int fame)
        {
            var abilityId = 0u;
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node.FameThreshold > fame)
                {
                    break;
                }

                if (node.Kind == SkillNodeKind.Active)
                {
                    abilityId = node.AbilityId;
                }
            }

            return abilityId;
        }

        /// <summary>Суммарная доля бонуса к урону от открытых пассивных узлов.</summary>
        /// <param name="fame">Накопленная Слава в ветке.</param>
        /// <returns>Доля, например 0.15.</returns>
        public float GetDamageBonus(int fame)
        {
            var total = 0f;
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node.FameThreshold > fame)
                {
                    break;
                }

                if (node.Kind == SkillNodeKind.Passive)
                {
                    total += node.DamageBonus;
                }
            }

            return total;
        }

        /// <summary>Суммарный плоский бонус к здоровью от открытых пассивных узлов.</summary>
        /// <param name="fame">Накопленная Слава в ветке.</param>
        /// <returns>Бонус к максимальному здоровью.</returns>
        public int GetMaxHpBonus(int fame)
        {
            var total = 0;
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node.FameThreshold > fame)
                {
                    break;
                }

                if (node.Kind == SkillNodeKind.Passive)
                {
                    total += node.MaxHpBonus;
                }
            }

            return total;
        }

        /// <summary>Суммарная доля бонуса к скорости от открытых пассивных узлов.</summary>
        /// <param name="fame">Накопленная Слава в ветке.</param>
        /// <returns>Доля, например 0.03.</returns>
        public float GetMoveSpeedBonus(int fame)
        {
            var total = 0f;
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node.FameThreshold > fame)
                {
                    break;
                }

                if (node.Kind == SkillNodeKind.Passive)
                {
                    total += node.MoveSpeedBonus;
                }
            }

            return total;
        }

        /// <summary>Сколько узлов ветки открыто при данной Славе.</summary>
        /// <param name="fame">Накопленная Слава в ветке.</param>
        /// <returns>Количество открытых узлов.</returns>
        public int GetUnlockedCount(int fame)
        {
            var count = 0;
            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].FameThreshold <= fame)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Следующий закрытый узел ветки.</summary>
        /// <param name="fame">Накопленная Слава в ветке.</param>
        /// <returns>Узел или <c>null</c>, если ветка открыта полностью.</returns>
        public SkillNode? GetNextNode(int fame)
        {
            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].FameThreshold > fame)
                {
                    return _nodes[i];
                }
            }

            return null;
        }
    }
}
