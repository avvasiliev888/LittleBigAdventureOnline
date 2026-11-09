using Client.Network;
using Simulation.Core;
using Shared;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Проверки контрактов M1: интерфейсы существуют и лежат в ожидаемых сборках,
    /// версия протокола не равна нулю. Полнота XML-документации гарантируется сборкой
    /// (<c>GenerateDocumentationFile</c> + <c>TreatWarningsAsErrors</c>, предупреждение CS1591).
    /// </summary>
    public sealed class ContractTests
    {
        [Fact]
        public void SimulationCore_ExposesRequiredInterfaces()
        {
            var assembly = typeof(ISimulation).Assembly;

            Assert.NotNull(assembly.GetType("Simulation.Core.ISimulation"));
            Assert.NotNull(assembly.GetType("Simulation.Core.ICommand"));
            Assert.NotNull(assembly.GetType("Simulation.Core.ISnapshotSerializer"));
        }

        [Fact]
        public void ClientNetwork_ExposesRequiredInterfaces()
        {
            var assembly = typeof(ITransport).Assembly;

            Assert.NotNull(assembly.GetType("Client.Network.ITransport"));
            Assert.NotNull(assembly.GetType("Client.Network.IInputSource"));
            Assert.NotNull(assembly.GetType("Client.Network.IView"));
        }

        [Fact]
        public void Protocol_HasNonZeroVersionAndMagic()
        {
            Assert.NotEqual(0, Protocol.Version);
            Assert.NotEqual(0, Protocol.Magic);
        }
    }
}
