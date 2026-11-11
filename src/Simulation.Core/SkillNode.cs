using System;

namespace Simulation.Core
{
    /// <summary>
    /// Узел дерева навыков (ADR-015): порог Славы в ветке архетипа, после которого узел открыт.
    /// Узел пассивный (бонус к характеристикам) или активный (способность вместо базовой).
    /// </summary>
    public sealed class SkillNode
    {
        /// <summary>Создаёт узел дерева навыков.</summary>
        /// <param name="id">Идентификатор узла, уникальный во всём дереве; положительный.</param>
        /// <param name="name">Читаемое имя узла.</param>
        /// <param name="fameThreshold">Порог Славы в ветке; неотрицательный.</param>
        /// <param name="kind">Вид узла: пассивный или активный.</param>
        /// <param name="abilityId">Способность активного узла; 0 для пассивного.</param>
        /// <param name="damageBonus">Доля бонуса к урону от пассивного узла (0.05 = +5%).</param>
        /// <param name="maxHpBonus">Плоский бонус к здоровью от пассивного узла.</param>
        /// <param name="moveSpeedBonus">Доля бонуса к скорости от пассивного узла.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если имя пусто или вид узла противоречит его полям.</exception>
        public SkillNode(
            uint id,
            string name,
            int fameThreshold,
            SkillNodeKind kind,
            uint abilityId = 0,
            float damageBonus = 0f,
            int maxHpBonus = 0,
            float moveSpeedBonus = 0f)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id узла должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Имя узла не может быть пустым.", nameof(name));
            }

            if (fameThreshold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fameThreshold),
                    fameThreshold,
                    "Порог Славы не может быть отрицательным.");
            }

            if (!(damageBonus >= 0f) || damageBonus > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(damageBonus),
                    damageBonus,
                    "Бонус к урону должен быть конечным числом в диапазоне 0..1.");
            }

            if (maxHpBonus < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxHpBonus),
                    maxHpBonus,
                    "Бонус к здоровью не может быть отрицательным.");
            }

            if (!(moveSpeedBonus >= 0f) || moveSpeedBonus > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(moveSpeedBonus),
                    moveSpeedBonus,
                    "Бонус к скорости должен быть конечным числом в диапазоне 0..1.");
            }

            if (kind == SkillNodeKind.Active)
            {
                if (abilityId == 0)
                {
                    throw new ArgumentException(
                        "Активный узел '" + name + "' обязан указывать способность.",
                        nameof(abilityId));
                }

                if (damageBonus > 0f || maxHpBonus != 0 || moveSpeedBonus > 0f)
                {
                    throw new ArgumentException(
                        "Активный узел '" + name + "' не даёт пассивных бонусов.",
                        nameof(damageBonus));
                }
            }
            else
            {
                if (abilityId != 0)
                {
                    throw new ArgumentException(
                        "Пассивный узел '" + name + "' не указывает способность.",
                        nameof(abilityId));
                }

                if (damageBonus <= 0f && maxHpBonus == 0 && moveSpeedBonus <= 0f)
                {
                    throw new ArgumentException(
                        "Пассивный узел '" + name + "' должен давать хотя бы один ненулевой бонус.",
                        nameof(damageBonus));
                }
            }

            Id = id;
            Name = name;
            FameThreshold = fameThreshold;
            Kind = kind;
            AbilityId = abilityId;
            DamageBonus = damageBonus;
            MaxHpBonus = maxHpBonus;
            MoveSpeedBonus = moveSpeedBonus;
        }

        /// <summary>Идентификатор узла, уникальный во всём дереве.</summary>
        public uint Id { get; }

        /// <summary>Читаемое имя узла.</summary>
        public string Name { get; }

        /// <summary>Порог Славы в ветке архетипа, открывающий узел.</summary>
        public int FameThreshold { get; }

        /// <summary>Вид узла.</summary>
        public SkillNodeKind Kind { get; }

        /// <summary>Способность активного узла; 0 для пассивного.</summary>
        public uint AbilityId { get; }

        /// <summary>Доля бонуса к урону.</summary>
        public float DamageBonus { get; }

        /// <summary>Плоский бонус к максимальному здоровью.</summary>
        public int MaxHpBonus { get; }

        /// <summary>Доля бонуса к скорости движения.</summary>
        public float MoveSpeedBonus { get; }
    }
}
