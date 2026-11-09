using System.Numerics;
using Client.Network;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M7: сглаживание локального игрока. Ключевые требования — нулевая задержка управления,
    /// ровная временная экстраполяция (dead reckoning) по скорости ввода и плавные коррекции
    /// реконсиляции без телепортов.
    /// </summary>
    public sealed class LocalPredictionSmootherTests
    {
        private const float Step = 0.2f;
        private const float Tick = 1f / 20f;

        private static readonly Vector3 Speed = new Vector3(4f, 0f, 0f);

        private static Vector3 Moving(float x) => new Vector3(x, 0f, 0f);

        [Fact]
        public void FirstSample_IsRenderedImmediately()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(1f), Speed);

            Assert.True(smoother.IsInitialized);
            Assert.Equal(Moving(1f), smoother.Position);
        }

        [Fact]
        public void Advance_ExtrapolatesByTime()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);

            smoother.Advance(Tick);

            Assert.Equal(Step, smoother.ExtrapolatedPosition.X, 4);
            Assert.Equal(Speed, smoother.Velocity);
        }

        [Fact]
        public void DeadReckoning_IsExactMidTick()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);
            smoother.Advance(Tick);
            smoother.AddSample(Moving(Step), Speed);

            smoother.Advance(0.025f);

            // «Тик 100.5»: предсказание 0.2, скорость 4 → ожидаем 0.3.
            Assert.Equal(0.3f, smoother.ExtrapolatedPosition.X, 4);
        }

        [Fact]
        public void SteadySamples_HaveNoVisualLag()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);

            for (var i = 1; i <= 10; i++)
            {
                smoother.Advance(Tick);
                smoother.AddSample(Moving(Step * i), Speed);

                Assert.Equal(Moving(Step * i), smoother.Position);
            }
        }

        [Fact]
        public void Stop_IsImmediate_WithoutOvershoot()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(Step), Speed);
            smoother.Advance(Tick);
            var before = smoother.Position.X;

            smoother.AddSample(Moving(Step), Vector3.Zero);

            // Капсула не уезжает вперёд после остановки: она там, где была.
            Assert.Equal(before, smoother.Position.X, 3);

            smoother.Advance(LocalPredictionSmoother.CorrectionSeconds * 3f);
            Assert.True(System.Math.Abs(smoother.Position.X - Step) < 0.05f, "после остановки капсула возвращается к предсказанной позиции");
        }

        [Fact]
        public void BackwardCorrection_KeepsVisualContinuity_ThenFades()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);
            smoother.Advance(Tick);
            smoother.AddSample(Moving(Step), Speed);
            smoother.Advance(Tick);
            smoother.AddSample(Moving(Step * 2f), Speed);
            smoother.Advance(Tick);

            var before = smoother.Position.X;

            // Сервер «поправил» позицию назад: без сглаживания был бы рывок.
            smoother.AddSample(Moving(Step * 2f - 0.5f), Speed);
            Assert.Equal(before, smoother.Position.X, 3);

            smoother.Advance(LocalPredictionSmoother.CorrectionSeconds * 3f);

            Assert.True(System.Math.Abs(smoother.Position.X - smoother.ExtrapolatedPosition.X) < 0.05f,
                "оффсет должен погаснуть, остаётся только экстраполяция");
        }

        [Fact]
        public void Teleport_SnapsImmediately()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);

            smoother.AddSample(Moving(10f), Speed);

            Assert.Equal(Moving(10f), smoother.Position);
        }

        [Fact]
        public void Extrapolation_StopsAfterOneTick()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);

            smoother.Advance(1f);

            Assert.Equal(4f * LocalPredictionSmoother.ExtrapolationSeconds, smoother.ExtrapolatedPosition.X, 4);
        }

        [Fact]
        public void Reset_StopsMoving()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.AddSample(Moving(0f), Speed);
            smoother.Advance(Tick);

            smoother.Reset(Moving(5f));
            smoother.Advance(Tick);

            Assert.Equal(Moving(5f), smoother.Position);
            Assert.Equal(Vector3.Zero, smoother.Velocity);
        }

        [Fact]
        public void Advance_BeforeAnySample_DoesNothing()
        {
            var smoother = new LocalPredictionSmoother();
            smoother.Advance(Tick);

            Assert.False(smoother.IsInitialized);
            Assert.Equal(Vector3.Zero, smoother.Position);
        }
    }
}
