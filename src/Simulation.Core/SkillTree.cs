using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Дерево навыков (ADR-015): ветки архетипов оружия из <c>config/skill-tree.json</c>.
    /// Узлы открываются автоматически по накопленной в ветке Славе; дерево не хранит прогресс —
    /// он живёт в <see cref="FameProgress"/> сущности, поэтому смена оружия его не сбрасывает.
    /// </summary>
    public sealed class SkillTree
    {
        private readonly Dictionary<string, SkillArchetype> _archetypes;

        /// <summary>Создаёт дерево навыков.</summary>
        /// <param name="archetypes">Ветки дерева; ключи и идентификаторы узлов уникальны.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="archetypes"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если ключи или идентификаторы узлов повторяются.</exception>
        public SkillTree(IReadOnlyList<SkillArchetype> archetypes)
        {
            if (archetypes == null)
            {
                throw new ArgumentNullException(nameof(archetypes));
            }

            _archetypes = new Dictionary<string, SkillArchetype>(StringComparer.Ordinal);
            var nodeIds = new HashSet<uint>();

            for (var i = 0; i < archetypes.Count; i++)
            {
                var archetype = archetypes[i];
                if (archetype == null)
                {
                    throw new ArgumentException("Дерево содержит null в позиции " + i + ".", nameof(archetypes));
                }

                if (_archetypes.ContainsKey(archetype.Key))
                {
                    throw new ArgumentException(
                        "Ветка '" + archetype.Key + "' объявлена дважды.",
                        nameof(archetypes));
                }

                for (var n = 0; n < archetype.Nodes.Count; n++)
                {
                    var node = archetype.Nodes[n];
                    if (!nodeIds.Add(node.Id))
                    {
                        throw new ArgumentException(
                            "Узел " + node.Id + " (" + node.Name + ") объявлен дважды во всём дереве.",
                            nameof(archetypes));
                    }
                }

                _archetypes.Add(archetype.Key, archetype);
            }
        }

        /// <summary>Пустое дерево (конфиг без прогрессии); допустимо для тестов и мира без веток.</summary>
        public static SkillTree Empty { get; } = new SkillTree(Array.Empty<SkillArchetype>());

        /// <summary>Ветки дерева в порядке конфига.</summary>
        public IReadOnlyCollection<SkillArchetype> Archetypes => _archetypes.Values;

        /// <summary>Количество веток.</summary>
        public int Count => _archetypes.Count;

        /// <summary>Есть ли ветка с таким ключом.</summary>
        /// <param name="archetypeKey">Ключ архетипа.</param>
        /// <returns><c>true</c>, если ветка объявлена.</returns>
        public bool Has(string archetypeKey)
        {
            return archetypeKey != null && _archetypes.ContainsKey(archetypeKey);
        }

        /// <summary>Возвращает ветку по ключу.</summary>
        /// <param name="archetypeKey">Ключ архетипа.</param>
        /// <returns>Ветка дерева.</returns>
        /// <exception cref="ArgumentException">Если ветки с таким ключом нет.</exception>
        public SkillArchetype Get(string archetypeKey)
        {
            if (archetypeKey != null && _archetypes.TryGetValue(archetypeKey, out var archetype))
            {
                return archetype;
            }

            throw new ArgumentException("Ветка '" + archetypeKey + "' отсутствует в дереве навыков.", nameof(archetypeKey));
        }

        /// <summary>Пытается получить ветку по ключу.</summary>
        /// <param name="archetypeKey">Ключ архетипа.</param>
        /// <param name="archetype">Найденная ветка или <c>null</c>.</param>
        /// <returns><c>true</c>, если ветка объявлена.</returns>
        public bool TryGet(string archetypeKey, out SkillArchetype? archetype)
        {
            if (archetypeKey != null && _archetypes.TryGetValue(archetypeKey, out var found))
            {
                archetype = found;
                return true;
            }

            archetype = null;
            return false;
        }

        /// <summary>Количество узлов во всех ветках.</summary>
        public int NodeCount
        {
            get
            {
                var total = 0;
                foreach (var archetype in _archetypes.Values)
                {
                    total += archetype.Nodes.Count;
                }

                return total;
            }
        }
    }
}
