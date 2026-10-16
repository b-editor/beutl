using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Build;

[TestFixture]
public class WindowsInstallerScriptTests
{
    [Test]
    public void SupportedTypes_ListsTheProjectFileExtension()
    {
        string installer = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "nukebuild", "beutl-setup.iss"));

        Assert.Multiple(() =>
        {
            Assert.That(installer, Does.Contain("#define MyAppAssocExt \".bep\""));
            Assert.That(installer, Does.Contain("\\SupportedTypes\"; ValueType: string; ValueName: \"{#MyAppAssocExt}\""));
            // .myp is the Inno Setup wizard's sample extension.
            Assert.That(installer, Does.Not.Contain(".myp"));
        });
    }
}
