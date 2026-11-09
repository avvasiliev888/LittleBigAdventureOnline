using System;
using System.Diagnostics;

namespace Server
{
    /// <summary>Источник времени серверного цикла: спрятан за интерфейсом, чтобы тест шёл без реальных пауз.</summary>
    public interface IClock
    {
        /// <summary>Миллисекунды с момента старта процесса.</summary>
        long ElapsedMilliseconds { get; }

        /// <summary>Приостанавливает поток на заданное время.</summary>
        /// <param name="milliseconds">Длительность паузы; значения меньше нуля игнорируются.</param>
        void Sleep(int milliseconds);
    }

    /// <summary>Реальные часы на <see cref="Stopwatch"/> и <see cref="System.Threading.Thread.Sleep(int)"/>.</summary>
    public sealed class SystemClock : IClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        /// <inheritdoc />
        public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

        /// <inheritdoc />
        public void Sleep(int milliseconds)
        {
            if (milliseconds > 0)
            {
                System.Threading.Thread.Sleep(milliseconds);
            }
        }
    }
}
