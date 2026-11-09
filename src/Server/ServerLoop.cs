using System;

namespace Server
{
    /// <summary>
    /// Серверный цикл с фиксированным тиком и компенсацией дрейфа (M4).
    /// Один <see cref="RunOnce"/> = приём пакетов, тик симуляции, пауза до следующего дедлайна.
    /// Дедлайн двигается на фиксированный интервал, поэтому случайные задержки не накапливаются.
    /// </summary>
    public sealed class ServerLoop
    {
        private readonly GameServer _server;
        private readonly IClock _clock;
        private readonly int _tickIntervalMs;
        private long _nextDeadlineMs;

        /// <summary>Создаёт цикл.</summary>
        /// <param name="server">Сервер, которому принадлежит симуляция.</param>
        /// <param name="clock">Источник времени (реальный или фейковый в тестах).</param>
        /// <param name="tickRate">Частота тика, Гц.</param>
        /// <exception cref="ArgumentNullException">Если аргумент равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если частота не положительна или больше 1000 Гц.</exception>
        public ServerLoop(GameServer server, IClock clock, int tickRate = 20)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            if (tickRate <= 0 || tickRate > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "TickRate должен быть в диапазоне 1..1000 Гц.");
            }

            TickRate = tickRate;
            _tickIntervalMs = 1000 / tickRate;
            _nextDeadlineMs = _clock.ElapsedMilliseconds;
        }

        /// <summary>Частота тика, Гц.</summary>
        public int TickRate { get; }

        /// <summary>Интервал тика в миллисекундах.</summary>
        public int TickIntervalMs => _tickIntervalMs;

        /// <summary>Сколько тиков выполнено циклом.</summary>
        public long TicksExecuted { get; private set; }

        /// <summary>Выполняет ровно один тик и паузу до следующего дедлайна.</summary>
        public void RunOnce()
        {
            _server.Poll();
            _server.Tick();
            TicksExecuted++;

            var now = _clock.ElapsedMilliseconds;
            if (_nextDeadlineMs <= now)
            {
                // Отстали больше чем на тик: перезаякориваемся, чтобы не копить догоняющие тики.
                _nextDeadlineMs = now;
            }

            var sleep = (int)(_nextDeadlineMs - now);
            _nextDeadlineMs += _tickIntervalMs;

            if (sleep > 0)
            {
                _clock.Sleep(sleep);
            }
        }
    }
}
