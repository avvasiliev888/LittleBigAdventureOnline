namespace Simulation.Core
{
    /// <summary>
    /// Команда крафта: создать предмет по рецепту из сырья в инвентаре (ADR-017).
    /// Сырьё списывается, предмет добавляется, Слава начисляется в ветку оружия в руке.
    /// </summary>
    public sealed class CraftCommand : ICommand
    {
        /// <summary>Создаёт команду крафта.</summary>
        /// <param name="recipeId">Идентификатор рецепта из <c>recipes.json</c>; положительный.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Если идентификатор рецепта равен нулю.</exception>
        public CraftCommand(uint recipeId, uint clientTick, uint sequence)
        {
            if (recipeId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(recipeId), recipeId, "RecipeId должен быть положительным.");
            }

            RecipeId = recipeId;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор рецепта.</summary>
        public uint RecipeId { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }

    /// <summary>
    /// Команда покупки у NPC-торговца (ADR-017): игрок платит серебро и получает предмет.
    /// </summary>
    public sealed class BuyCommand : ICommand
    {
        /// <summary>Создаёт команду покупки.</summary>
        /// <param name="traderId">Идентификатор торговца из <c>npc-traders.json</c>; положительный.</param>
        /// <param name="itemId">Идентификатор покупаемого предмета; положительный.</param>
        /// <param name="quantity">Сколько купить; положительное.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Если идентификатор или количество не положительны.</exception>
        public BuyCommand(uint traderId, uint itemId, int quantity, uint clientTick, uint sequence)
        {
            if (traderId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(traderId), traderId, "TraderId должен быть положительным.");
            }

            if (itemId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(itemId), itemId, "ItemId должен быть положительным.");
            }

            if (quantity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество должно быть положительным.");
            }

            TraderId = traderId;
            ItemId = itemId;
            Quantity = quantity;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор торговца.</summary>
        public uint TraderId { get; }

        /// <summary>Идентификатор покупаемого предмета.</summary>
        public uint ItemId { get; }

        /// <summary>Количество.</summary>
        public int Quantity { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }

    /// <summary>
    /// Команда продажи NPC-торговцу (ADR-017): игрок отдаёт предмет и получает серебро.
    /// </summary>
    public sealed class SellCommand : ICommand
    {
        /// <summary>Создаёт команду продажи.</summary>
        /// <param name="traderId">Идентификатор торговца из <c>npc-traders.json</c>; положительный.</param>
        /// <param name="itemId">Идентификатор продаваемого предмета; положительный.</param>
        /// <param name="quantity">Сколько продать; положительное.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Если идентификатор или количество не положительны.</exception>
        public SellCommand(uint traderId, uint itemId, int quantity, uint clientTick, uint sequence)
        {
            if (traderId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(traderId), traderId, "TraderId должен быть положительным.");
            }

            if (itemId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(itemId), itemId, "ItemId должен быть положительным.");
            }

            if (quantity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество должно быть положительным.");
            }

            TraderId = traderId;
            ItemId = itemId;
            Quantity = quantity;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор торговца.</summary>
        public uint TraderId { get; }

        /// <summary>Идентификатор продаваемого предмета.</summary>
        public uint ItemId { get; }

        /// <summary>Количество.</summary>
        public int Quantity { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
