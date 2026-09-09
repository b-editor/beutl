using Beutl.Testing.Headless;
using Avalonia.Headless;

namespace Beutl.HeadlessUITests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public async Task SetUp()
    {
        BeutlHomeIsolation.Begin("beutl-shell-e2e");
        // Plain NUnit tests can construct services that access Dispatcher.UIThread.
        // Initialize the shared headless application before any such test can pin
        // that singleton to NullDispatcherImpl (which cannot run PushFrame).
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AssemblySetUp).Assembly)
            .Dispatch(() => { }, CancellationToken.None);
    }

    [OneTimeTearDown]
    public void TearDown() => BeutlHomeIsolation.End();
}
