using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Слава сущности по архетипам оружия (ADR-015). Слава копится в ветке того оружия,
    /// с которым игрок охотился или собирал ресурсы; смена оружия не сбрасывает прогресс.
    /// </summary>
    public sealed class FameProgress
    {
        private readonly SortedDictionary<string, int> _fame = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Слава в ветке архетипа.</summary>
        /// <param name="archetypeKey">Ключ архетипа.</param>
        /// <returns>Накопленная Слава; 0, если в этой ветке её ещё нет.</returns>
        /// <exception cref="ArgumentException">Если ключ пуст.</exception>
        public int Get(string archetypeKey)
        {
            if (string.IsNullOrWhiteSpace(archetypeKey))
            {
                throw new ArgumentException("Ключ архетипа не может быть пустым.", nameof(archetypeKey));
            }

            return _fame.TryGetValue(archetypeKey, out var fame) ? fame : 0;
        }

        /// <summary>Начисляет Славу в ветку архетипа.</summary>
        /// <param name="archetypeKey">Ключ архетипа.</param>
        /// <param name="amount">Сколько начислить; неотрицательное.</param>
        /// <returns>Новое значение Славы в ветке.</returns>
        /// <exception cref="ArgumentException">Если ключ пуст.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если количество отрицательное.</exception>
        public int Add(string archetypeKey, int amount)
        {
            if (string.IsNullOrWhiteSpace(archetypeKey))
            {
                throw new ArgumentException("Ключ архетипа не может быть пустым.", nameof(archetypeKey));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Слава не может быть отрицательной.");
            }

            var total = Get(archetypeKey) + amount;
            _fame[archetypeKey] = total;
            return total;
        }

        /// <summary>Суммарная Слава по всем веткам.</summary>
        public int Total
        {
            get
            {
                var total = 0;
                foreach (var pair in _fame)
                {
                    total += pair.Value;
                }

                return total;
            }
        }

        /// <summary>Слава по веткам в детерминированном порядке ключей (для снапшота и UI).</summary>
        public IReadOnlyDictionary<string, int> All => _fame;

        /// <summary>Есть ли Слава хотя бы в одной ветке.</summary>
        public bool IsEmpty => _fame.Count == 0;
    }
}
