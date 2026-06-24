using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp() => BeutlHomeIsolation.Begin("beutl-shell-e2e");

    [OneTimeTearDown]
    public void TearDown() => BeutlHomeIsolation.End();
}
