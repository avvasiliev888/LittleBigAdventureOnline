using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server
{
    /// <summary>
    /// Ring-buffer метрик длительности серверного тика (M11). Хранит последние
    /// <see cref="Capacity"/> измерений, чтобы долгоживущий сервер не рос в памяти.
    /// </summary>
    public sealed class ServerMetrics
    {
        private readonly double[] _samples;
        private int _next;
        private int _count;

        /// <summary>Создаёт буфер метрик.</summary>
        /// <param name="capacity">Максимум хранимых измерений; по умолчанию 12 000 (10 минут при 20 Гц).</param>
        /// <exception cref="ArgumentOutOfRangeException">Если ёмкость не положительна.</exception>
        public ServerMetrics(int capacity = 12000)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Ёмкость должна быть положительной.");
            }

            _samples = new double[capacity];
        }

        /// <summary>Максимум хранимых измерений.</summary>
        public int Capacity => _samples.Length;

        /// <summary>Сколько измерений сейчас в буфере.</summary>
        public int SampleCount => _count;

        /// <summary>Добавляет длительность тика в миллисекундах.</summary>
        /// <param name="milliseconds">Неотрицательное конечное число.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если значение NaN, бесконечно или отрицательно.</exception>
        public void Record(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(milliseconds),
                    milliseconds,
                    "Длительность тика должна быть конечной и неотрицательной.");
            }

            _samples[_next] = milliseconds;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length)
            {
                _count++;
            }
        }

        /// <summary>Очищает буфер.</summary>
        public void Reset()
        {
            _next = 0;
            _count = 0;
        }

        /// <summary>Считает среднее, p50, p99 и максимум по текущему окну.</summary>
        public ServerMetricSnapshot Snapshot()
        {
            if (_count == 0)
            {
                return new ServerMetricSnapshot(0, 0d, 0d, 0d, 0d);
            }

            var sorted = new double[_count];
            Array.Copy(_samples, sorted, _count);
            Array.Sort(sorted);

            var sum = 0d;
            for (var i = 0; i < sorted.Length; i++)
            {
                sum += sorted[i];
            }

            return new ServerMetricSnapshot(
                _count,
                sum / _count,
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.99),
                sorted[sorted.Length - 1]);
        }

        /// <summary>Форматирует текущие метрики одной строкой (InvariantCulture, удобно парсить).</summary>
        /// <param name="label">Префикс, например <c>tick</c>.</param>
        public string Format(string label = "tick")
        {
            var snapshot = Snapshot();
            var culture = CultureInfo.InvariantCulture;
            return label + " metrics: n=" + snapshot.Count.ToString(culture)
                + " avg=" + snapshot.AverageMs.ToString("0.###", culture) + "ms"
                + " p50=" + snapshot.P50Ms.ToString("0.###", culture) + "ms"
                + " p99=" + snapshot.P99Ms.ToString("0.###", culture) + "ms"
                + " max=" + snapshot.MaxMs.ToString("0.###", culture) + "ms";
        }

        private static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            var rank = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
        }
    }

    /// <summary>Снимок метрик тика.</summary>
    public readonly struct ServerMetricSnapshot
    {
        /// <summary>Создаёт снимок.</summary>
        public ServerMetricSnapshot(int count, double averageMs, double p50Ms, double p99Ms, double maxMs)
        {
            Count = count;
            AverageMs = averageMs;
            P50Ms = p50Ms;
            P99Ms = p99Ms;
            MaxMs = maxMs;
        }

        /// <summary>Число измерений.</summary>
        public int Count { get; }

        /// <summary>Средняя длительность, мс.</summary>
        public double AverageMs { get; }

        /// <summary>Медиана, мс.</summary>
        public double P50Ms { get; }

        /// <summary>99-й перцентиль, мс.</summary>
        public double P99Ms { get; }

        /// <summary>Максимум, мс.</summary>
        public double MaxMs { get; }
    }
}
