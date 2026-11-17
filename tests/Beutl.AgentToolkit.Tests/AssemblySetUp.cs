using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.AgentToolkit.Tests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        var configuration = new GlobalConfiguration();
        DecoderRegistry.Initialize(configuration.ExtensionConfig);
        FontManager.Instance.Initialize(configuration.FontConfig);
    }
}
