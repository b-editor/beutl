using Beutl.Configuration;
using Beutl.Testing.Headless;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests;

[SetUpFixture]
public sealed class AssemblySetUp
{
    [OneTimeSetUp]
    public void SetUp()
    {
        string home = BeutlHomeIsolation.Begin("beutl-unit");

        // Project loading can initialize FontManager before TypefaceProvider registers its
        // fixtures. Keep host-installed families/styles from taking their registry slots.
        GlobalConfiguration.Instance.FontConfig.FontDirectories.Clear();

        Assert.That(
            GlobalConfiguration.DefaultFilePath,
            Is.EqualTo(Path.Combine(home, "settings.json")));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try
        {
            BeutlHomeIsolation.End();
        }
        finally
        {
            VulkanTestEnvironment.AssertNoUnattributedValidationErrors();
        }
    }
}
