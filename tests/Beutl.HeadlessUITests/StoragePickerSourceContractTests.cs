namespace Beutl.HeadlessUITests;

[TestFixture]
public class StoragePickerSourceContractTests
{
    [Test]
    public void Storage_picker_call_sites_do_not_require_window_visual_root()
    {
        string root = FindRepositoryRoot();
        string[] files =
        [
            "src/Beutl/Views/MainView.axaml.InitializeMenuBar.cs",
            "src/Beutl/Views/Dialogs/CreateNewProject.axaml.cs",
            "src/Beutl/Views/Dialogs/CreateNewScene.axaml.cs",
            "src/Beutl/Pages/SettingsPages/FontSettingsPage.axaml.cs",
            "src/Beutl.Controls/PropertyEditors/StorageFileEditor.cs",
            "src/Beutl.Controls/FileInputArea.cs",
            "src/Beutl/Views/Tools/OutputView.axaml.cs",
        ];

        Assert.Multiple(() =>
        {
            foreach (string file in files)
            {
                string text = File.ReadAllText(Path.Combine(root, file));
                Assert.That(text, Does.Not.Contain("VisualRoot is Window"), file);
                Assert.That(text, Does.Not.Contain("VisualRoot is not Window"), file);
                Assert.That(text, Does.Not.Contain("VisualRoot is TopLevel"), file);
            }
        });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
