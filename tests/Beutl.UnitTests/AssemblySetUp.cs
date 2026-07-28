using Beutl.Configuration;
using Beutl.Testing.Headless;

namespace Beutl.UnitTests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        string home = BeutlHomeIsolation.Begin("beutl-unit");

        Assert.That(
            GlobalConfiguration.DefaultFilePath,
            Is.EqualTo(Path.Combine(home, "settings.json")));
    }

    [OneTimeTearDown]
    public void TearDown() => BeutlHomeIsolation.End();
}
