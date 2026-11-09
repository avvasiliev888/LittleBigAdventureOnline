using System;
using System.Numerics;

namespace Client.Network
{
    /// <summary>
    /// Сглаживание визуальной позиции локального игрока (M7): позиция рисуется без задержки буфера —
    /// сразу по последнему предсказанию, — а между тиками экстраполируется по времени (dead reckoning)
    /// по <b>скорости из ввода</b>: <c>position + velocity * timeSinceSample</c>, не дальше одного тика.
    /// </summary>
    /// <remarks>
    /// Сэмплы приходят каждый клиентский тик (20 Гц) и не зависят от джиттера сети: серверные
    /// снапшоты лишь поправляют предсказание. Скорость берётся из отправленной команды (а не из
    /// разности сэмплов), поэтому остановка мгновенная, а мусорные скачки позиции не превращаются
    /// в ложную скорость. Коррекции не телепортируют капсулу: расхождение уходит в визуальный
    /// оффсет и затухает за <see cref="CorrectionSeconds"/>. Удалённые игроки рисуются отдельно,
    /// через буфер снапшотов.
    /// </remarks>
    public sealed class LocalPredictionSmoother
    {
        /// <summary>Сколько живёт визуальный оффсет коррекции, секунды.</summary>
        public const float CorrectionSeconds = 0.12f;

        /// <summary>Горизонт экстраполяции: один тик (50 мс) — больше без данных не предсказываем.</summary>
        public const float ExtrapolationSeconds = 0.05f;

        /// <summary>Разрыв больше этого значения считается телепортом (респавн, крупная коррекция).</summary>
        public const float TeleportDistance = 2f;

        /// <summary>Правдоподобный предел скорости (единиц в секунду) — защита от мусорных сэмплов.</summary>
        public const float MaxSpeed = 6f;

        /// <summary>Расхождение меньше этого не гасим — это погрешность float, а не коррекция.</summary>
        public const float CorrectionThreshold = 0.02f;

        private Vector3 _position;
        private Vector3 _lastSample;
        private Vector3 _velocity;
        private Vector3 _correction;
        private float _sinceSample;
        private bool _initialized;

        /// <summary>Инициализирован ли сглаживатель (было ли хоть одно предсказание).</summary>
        public bool IsInitialized => _initialized;

        /// <summary>Позиция для отрисовки: предсказание + экстраполяция по времени + затухающий оффсет.</summary>
        public Vector3 Position => ExtrapolatedPosition + _correction;

        /// <summary>Позиция предсказания без визуального оффсета (для диагностики и тестов).</summary>
        public Vector3 ExtrapolatedPosition
        {
            get
            {
                var horizon = MathF.Min(_sinceSample, ExtrapolationSeconds);
                return _position + (_velocity * horizon);
            }
        }

        /// <summary>Текущая скорость экстраполяции, единиц в секунду.</summary>
        public Vector3 Velocity => _velocity;

        /// <summary>Сброс состояния и мгновенная установка позиции (первое предсказание, респавн).</summary>
        /// <param name="position">Позиция для отрисовки.</param>
        public void Reset(Vector3 position)
        {
            _position = position;
            _lastSample = position;
            _velocity = Vector3.Zero;
            _correction = Vector3.Zero;
            _sinceSample = 0f;
            _initialized = true;
        }

        /// <summary>
        /// Принимает предсказанную позицию и скорость из текущего ввода. Видимая позиция меняется
        /// мгновенно (нулевая задержка), а «мы уехали вперёд» гасится оффсетом плавно.
        /// </summary>
        /// <param name="position">Предсказанная позиция на текущий клиентский тик.</param>
        /// <param name="velocity">Скорость из отправленной команды (или ноль, если ввода нет).</param>
        public void AddSample(Vector3 position, Vector3 velocity)
        {
            if (!_initialized || Vector3.Distance(position, _lastSample) > TeleportDistance)
            {
                Reset(position);
                _velocity = ClampSpeed(velocity);
                return;
            }

            var previousRender = _position + (_velocity * MathF.Min(_sinceSample, ExtrapolationSeconds));
            var mismatch = previousRender - position;

            // Гасим только «мы уехали вперёд» (коррекция назад или остановка). Обычное
            // ускорение с нуля — не расхождение: капсула должна тронуться сразу, без лага.
            if (mismatch.Length() > CorrectionThreshold && Dot(mismatch, _velocity) > 0f)
            {
                _correction += mismatch;
            }

            _velocity = ClampSpeed(velocity);
            _position = position;
            _lastSample = position;
            _sinceSample = 0f;
        }

        /// <summary>
        /// Продвигает время на кадр: экстраполяция по времени и затухание визуального оффсета.
        /// </summary>
        /// <param name="deltaSeconds">Время кадра, секунды.</param>
        public void Advance(float deltaSeconds)
        {
            if (!_initialized || deltaSeconds <= 0f)
            {
                return;
            }

            _sinceSample += deltaSeconds;
            _correction *= MathF.Exp(-deltaSeconds / CorrectionSeconds);
        }

        private static float Dot(Vector3 a, Vector3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        private static Vector3 ClampSpeed(Vector3 velocity)
        {
            var speed = velocity.Length();
            return speed > MaxSpeed ? velocity * (MaxSpeed / speed) : velocity;
        }
    }
}
