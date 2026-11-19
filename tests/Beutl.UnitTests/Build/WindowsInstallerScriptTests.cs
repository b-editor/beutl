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

    [Test]
    public void InstallModeDialog_RecommendsTheCurrentUser()
    {
        string installer = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "nukebuild", "beutl-setup.iss"));

        Assert.Multiple(() =>
        {
            Assert.That(installer, Does.Contain("\nPrivilegesRequired=lowest"));
            Assert.That(installer, Does.Contain("\nPrivilegesRequiredOverridesAllowed=dialog"));
            // An update must keep the mode of the installation it replaces without asking.
            Assert.That(installer, Does.Not.Contain("UsePreviousPrivileges=no"));
        });
    }

    // UpdateDialogViewModel starts the installer with /SILENT /BeutlPid=<pid> and, to restart, /BeutlRelaunch=1.
    [Test]
    public void SilentUpdate_WaitsForBeutlAndCanStartItAgain()
    {
        string installer = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "nukebuild", "beutl-setup.iss"));

        Assert.Multiple(() =>
        {
            // The GPL page cannot be accepted without the user, so a silent install must skip it.
            Assert.That(installer, Does.Contain("Result := (PageID = GPLLicensePage.ID) and WizardSilent;"));
            Assert.That(installer, Does.Contain("{param:BeutlPid|0}"));
            Assert.That(installer, Does.Contain("function PrepareToInstall(var NeedsRestart: Boolean): String;"));
            Assert.That(installer, Does.Contain("{param:BeutlRelaunch|0}"));
            Assert.That(installer, Does.Contain("Flags: nowait postinstall skipifnotsilent; Check: ShouldRelaunch"));
            // Older versions of Beutl still show the wizard and pass this when the user is quitting.
            Assert.That(installer, Does.Contain("{param:BeutlOnQuit|0}"));
        });
    }
}
