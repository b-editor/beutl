using Beutl.Testing.Headless;

namespace Beutl.E2ETests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        BeutlHomeIsolation.Begin("beutl-e2e");
        var configuration = new Beutl.Configuration.GlobalConfiguration();
        Beutl.Media.Decoding.DecoderRegistry.Initialize(configuration.ExtensionConfig);
        Beutl.Media.FontManager.Instance.Initialize(configuration.FontConfig);
    }

    [OneTimeTearDown]
    public void TearDown() => BeutlHomeIsolation.End();
}
