using System.Numerics;

namespace Client.Network
{
    /// <summary>
    /// Сущность, восстановленная из снапшота для отрисовки: позиция интерполирована,
    /// дискретные поля (HP, ресурс, флаги) взяты из более старого снапшота пары.
    /// </summary>
    public readonly struct InterpolatedEntity
    {
        /// <summary>Создаёт снимок сущности для отрисовки.</summary>
        /// <param name="id">Идентификатор сущности.</param>
        /// <param name="actorId">Тип актора.</param>
        /// <param name="position">Интерполированная позиция.</param>
        /// <param name="hp">Здоровье.</param>
        /// <param name="maxHp">Максимум здоровья.</param>
        /// <param name="resource">Ресурс.</param>
        /// <param name="maxResource">Максимум ресурса.</param>
        /// <param name="isDead">Признак смерти.</param>
        public InterpolatedEntity(
            uint id,
            uint actorId,
            Vector3 position,
            int hp,
            int maxHp,
            int resource,
            int maxResource,
            bool isDead)
        {
            Id = id;
            ActorId = actorId;
            Position = position;
            Hp = hp;
            MaxHp = maxHp;
            Resource = resource;
            MaxResource = maxResource;
            IsDead = isDead;
        }

        /// <summary>Идентификатор сущности.</summary>
        public uint Id { get; }

        /// <summary>Тип актора.</summary>
        public uint ActorId { get; }

        /// <summary>Позиция для отрисовки.</summary>
        public Vector3 Position { get; }

        /// <summary>Здоровье.</summary>
        public int Hp { get; }

        /// <summary>Максимум здоровья.</summary>
        public int MaxHp { get; }

        /// <summary>Текущий ресурс.</summary>
        public int Resource { get; }

        /// <summary>Максимум ресурса.</summary>
        public int MaxResource { get; }

        /// <summary>Мертва ли сущность.</summary>
        public bool IsDead { get; }
    }
}
