using System;
using System.Numerics;

namespace Server
{
    /// <summary>
    /// Политики сервера (не геймплейные числа): частота тика, лимиты ввода, раскладка спавна.
    /// Геймплейные параметры живут в JSON-конфиге (ADR-005).
    /// </summary>
    public sealed class ServerOptions
    {
        /// <summary>Частота серверного тика, Гц.</summary>
        public int TickRate { get; init; } = 20;

        /// <summary>Максимум команд от одного клиента за тик (защита от флуда).</summary>
        public int MaxCommandsPerTick { get; init; } = 5;

        /// <summary>
        /// Допустимая длина вектора направления движения. Клиент обязан присылать единичный вектор,
        /// запас 5% — на ошибку нормализации; больше — попытка ускориться читом.
        /// </summary>
        public float MaxDirectionMagnitude { get; init; } = 1.05f;

        /// <summary>Шаг раскладки точек появления вдоль оси X, единицы мира.</summary>
        public float SpawnSpacing { get; init; } = 2f;

        /// <summary>
        /// Заспавнить тренировочный манекен (M10.7). Включается только серверной точкой входа;
        /// unit-тесты по умолчанию работают без него.
        /// </summary>
        public bool SpawnTrainingDummy { get; init; }

        /// <summary>Идентификатор сущности манекена (по умолчанию 2 — рядом с первым игроком).</summary>
        public uint TrainingDummyEntityId { get; init; } = 2;

        /// <summary>Тип актора манекена из <c>actors.json</c>.</summary>
        public uint TrainingDummyActorId { get; init; } = 2;

        /// <summary>Точка спавна манекена; он медленно патрулирует вокруг неё по X.</summary>
        public Vector3 TrainingDummySpawn { get; init; } = new Vector3(4f, 0f, 0f);

        /// <summary>Логировать каждый тик (для демо; в тестах выключено).</summary>
        public bool LogTicks { get; init; }
    }
}
