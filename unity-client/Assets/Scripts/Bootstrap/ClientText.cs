using System;
using System.Collections.Generic;

namespace Albion.Client
{
    /// <summary>
    /// Единственное место, где внутренние ключи контента превращаются в русские названия
    /// для игрока. Ключи остаются английскими в config/*.json и в протоколе: их нельзя
    /// переименовывать без миграции сохранений и серверных данных. Здесь — только
    /// отображение, поэтому сервер и симуляция не зависят от языка интерфейса.
    /// </summary>
    public static class ClientText
    {
        private static readonly Dictionary<string, string> ItemNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["wood"] = "Древесина",
                ["ore"] = "Руда",
                ["oak_log"] = "Дубовое бревно",
                ["iron_ore"] = "Железная руда",
                ["ash_log"] = "Ясеневое бревно",
                ["steel_ore"] = "Стальная руда",
                ["ironwood_log"] = "Железное дерево",
                ["mithril_ore"] = "Мифриловая руда",
                ["rusty_sword"] = "Ржавый меч",
                ["traveler_chest"] = "Куртка путешественника",
                ["traveler_legs"] = "Штаны путешественника",
                ["traveler_boots"] = "Сапоги путешественника",
                ["hunter_bow"] = "Охотничий лук",
                ["apprentice_staff"] = "Посох ученика",
                ["iron_sword"] = "Железный меч",
                ["reinforced_chest"] = "Укреплённая куртка",
                ["reinforced_legs"] = "Укреплённые штаны",
                ["swift_boots"] = "Быстрые сапоги",
                ["iron_bow"] = "Железный лук",
                ["iron_staff"] = "Железный посох",
                ["steel_sword"] = "Стальной меч",
                ["steel_chest"] = "Стальная куртка",
                ["steel_legs"] = "Стальные штаны",
                ["steel_boots"] = "Стальные сапоги",
                ["steel_bow"] = "Стальной лук",
                ["steel_staff"] = "Стальной посох",
                ["mithril_sword"] = "Мифриловый меч",
                ["mithril_chest"] = "Мифриловая куртка",
                ["mithril_legs"] = "Мифриловые штаны",
                ["mithril_boots"] = "Мифриловые сапоги",
                ["mithril_bow"] = "Мифриловый лук",
                ["mithril_staff"] = "Мифриловый посох",
            };

        private static readonly Dictionary<string, string> ZoneNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["blue"] = "Синяя зона",
                ["yellow"] = "Жёлтая зона",
                ["red"] = "Красная зона",
            };

        private static readonly Dictionary<string, string> TraderNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["blue_merchant"] = "Синий торговец",
                ["yellow_merchant"] = "Жёлтый торговец",
                ["red_merchant"] = "Красный торговец",
            };

        /// <summary>Русское название предмета по его ключу из items.json.</summary>
        public static string Item(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Неизвестный предмет";
            }

            return ItemNames.TryGetValue(key, out var name) ? name : "Неизвестный предмет";
        }

        /// <summary>Русское название зоны по её ключу из zones.json.</summary>
        public static string Zone(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Неизвестная зона";
            }

            return ZoneNames.TryGetValue(key, out var name) ? name : "Неизвестная зона";
        }

        /// <summary>Русское название торговца по его ключу из npc-traders.json.</summary>
        public static string Trader(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Торговец";
            }

            return TraderNames.TryGetValue(key, out var name) ? name : "Торговец";
        }

        /// <summary>Русское название слота экипировки для инвентаря.</summary>
        public static string Slot(string key)
        {
            switch (key)
            {
                case "mainHand": return "Оружие";
                case "chest": return "Нагрудник";
                case "legs": return "Штаны";
                case "feet": return "Сапоги";
                default: return "Снаряжение";
            }
        }
    }
}
