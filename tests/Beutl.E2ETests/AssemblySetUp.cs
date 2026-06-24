using Beutl.Testing.Headless;

namespace Beutl.E2ETests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp() => BeutlHomeIsolation.Begin("beutl-e2e");

    [OneTimeTearDown]
    public void TearDown() => BeutlHomeIsolation.End();
}
