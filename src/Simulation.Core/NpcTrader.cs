using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Цена предмета у NPC-торговца (ADR-017): сколько игрок платит за покупку и получает за продажу.
    /// Спред узкий и фиксированный: <see cref="BuyPrice"/> строго больше <see cref="SellPrice"/>,
    /// иначе «купи — продай» давало бы бесконечное серебро.
    /// </summary>
    public sealed class TraderPrice
    {
        /// <summary>Создаёт цену предмета.</summary>
        /// <param name="itemId">Идентификатор предмета; положительный.</param>
        /// <param name="buyPrice">Цена покупки игроком; положительная.</param>
        /// <param name="sellPrice">Цена продажи игроком; положительная и меньше цены покупки.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если цены или идентификатор не положительны.</exception>
        /// <exception cref="ArgumentException">Если цена продажи не меньше цены покупки.</exception>
        public TraderPrice(uint itemId, int buyPrice, int sellPrice)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id предмета должен быть положительным.");
            }

            if (buyPrice <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(buyPrice), buyPrice, "Цена покупки должна быть положительной.");
            }

            if (sellPrice <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sellPrice), sellPrice, "Цена продажи должна быть положительной.");
            }

            if (sellPrice >= buyPrice)
            {
                throw new ArgumentException(
                    "Цена продажи (" + sellPrice + ") должна быть меньше цены покупки (" + buyPrice + ").",
                    nameof(sellPrice));
            }

            ItemId = itemId;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
        }

        /// <summary>Идентификатор предмета.</summary>
        public uint ItemId { get; }

        /// <summary>Сколько игрок платит NPC за единицу.</summary>
        public int BuyPrice { get; }

        /// <summary>Сколько NPC платит игроку за единицу.</summary>
        public int SellPrice { get; }
    }

    /// <summary>
    /// NPC-торговец из <c>config/npc-traders.json</c> (ADR-017): стоит в своей зоне, покупает и продаёт
    /// предметы по фиксированным ценам. Доступен игроку в радиусе <see cref="Range"/>.
    /// </summary>
    public sealed class NpcTrader
    {
        private readonly IReadOnlyList<TraderPrice> _prices;

        /// <summary>Создаёт NPC-торговца.</summary>
        /// <param name="id">Идентификатор торговца, уникальный в конфиге; положительный.</param>
        /// <param name="key">Читаемый ключ (например, <c>blue_merchant</c>).</param>
        /// <param name="zoneKey">Ключ зоны, в которой стоит торговец; зона должна существовать.</param>
        /// <param name="x">Мировая координата X; конечная.</param>
        /// <param name="z">Мировая координата Z; конечная.</param>
        /// <param name="range">Радиус взаимодействия; положительный.</param>
        /// <param name="prices">Прайс-лист; непустой список без повторов предметов.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="prices"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если ключ пуст или предметы в прайсе повторяются.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если число вне допустимого диапазона.</exception>
        public NpcTrader(
            uint id,
            string key,
            string zoneKey,
            float x,
            float z,
            float range,
            IReadOnlyList<TraderPrice> prices)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id торговца должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ торговца не может быть пустым.", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(zoneKey))
            {
                throw new ArgumentException("Ключ зоны торговца не может быть пустым.", nameof(zoneKey));
            }

            if (!IsFinite(x) || !IsFinite(z))
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, "Координаты торговца должны быть конечными числами.");
            }

            if (!(range > 0f) || float.IsInfinity(range))
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Радиус торговца должен быть конечным положительным числом.");
            }

            if (prices == null)
            {
                throw new ArgumentNullException(nameof(prices));
            }

            if (prices.Count == 0)
            {
                throw new ArgumentException("У торговца '" + key + "' должен быть хотя бы один товар.", nameof(prices));
            }

            var seen = new HashSet<uint>();
            var copy = new List<TraderPrice>(prices.Count);
            for (var i = 0; i < prices.Count; i++)
            {
                var price = prices[i];
                if (price == null)
                {
                    throw new ArgumentException("Прайс торговца '" + key + "' содержит null в позиции " + i + ".", nameof(prices));
                }

                if (!seen.Add(price.ItemId))
                {
                    throw new ArgumentException(
                        "Прайс торговца '" + key + "' повторяет предмет " + price.ItemId + ".",
                        nameof(prices));
                }

                copy.Add(price);
            }

            Id = id;
            Key = key;
            ZoneKey = zoneKey;
            Position = new Vector3(x, 0f, z);
            Range = range;
            _prices = copy.AsReadOnly();
        }

        /// <summary>Идентификатор торговца.</summary>
        public uint Id { get; }

        /// <summary>Читаемый ключ торговца.</summary>
        public string Key { get; }

        /// <summary>Ключ зоны, в которой стоит торговец.</summary>
        public string ZoneKey { get; }

        /// <summary>Позиция торговца в мире (Y = 0).</summary>
        public Vector3 Position { get; }

        /// <summary>Радиус взаимодействия.</summary>
        public float Range { get; }

        /// <summary>Прайс-лист торговца.</summary>
        public IReadOnlyList<TraderPrice> Prices => _prices;

        /// <summary>Находит цену предмета в прайсе.</summary>
        /// <param name="itemId">Идентификатор предмета.</param>
        /// <returns>Цена или <c>null</c>, если торговец этот предмет не торгует.</returns>
        public TraderPrice? FindPrice(uint itemId)
        {
            for (var i = 0; i < _prices.Count; i++)
            {
                if (_prices[i].ItemId == itemId)
                {
                    return _prices[i];
                }
            }

            return null;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
