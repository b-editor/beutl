using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class StoragePickerWindowTests
{
    [AvaloniaTest]
    public void Font_settings_uses_the_host_window_folder_picker()
    {
        using var viewModel = new FontSettingsPageViewModel();
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        storage.Setup(x => x.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ReturnsAsync(Array.Empty<IStorageFolder>());
        var view = new FontSettingsPage { DataContext = viewModel };
        var window = new Window { Content = view, Width = 500, Height = 400 };
        TestStorageProviderFactory.SetProvider(window, storage.Object);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            view.AddClick(view, new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();

            storage.Verify(x => x.OpenFolderPickerAsync(It.Is<FolderPickerOpenOptions>(options => options.AllowMultiple)), Times.Once);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Main_menu_file_project_import_and_export_commands_open_the_host_window_picker()
    {
        await TestReset.ResetShellAsync();
        string projectPath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "picker-project", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectPath);
        await TestShell.Project.CreateProject(640, 480, new Rational(30), 44100, "picker-project", projectPath);
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        storage.Setup(x => x.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()))
            .ReturnsAsync(Array.Empty<IStorageFile>());
        storage.Setup(x => x.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()))
            .ReturnsAsync((IStorageFile?)null);
        var window = new Window { Width = 800, Height = 600 };
        TestStorageProviderFactory.SetProvider(window, storage.Object);
        // Open the test host before attaching MainView, so it doesn't run the real App startup.
        window.Show();
        var view = new MainView { DataContext = TestShell.MainViewModel };
        try
        {
            window.Content = view;
            HeadlessTestHelpers.Render();

            await TestShell.MainViewModel.MenuBar.OpenFile.ExecuteAsync();
            await TestShell.MainViewModel.MenuBar.OpenProject.ExecuteAsync();
            await TestShell.MainViewModel.MenuBar.ImportProject.ExecuteAsync();
            await TestShell.MainViewModel.MenuBar.ExportProject.ExecuteAsync();

            storage.Verify(x => x.OpenFilePickerAsync(It.IsAny<FilePickerOpenOptions>()), Times.Exactly(3));
            storage.Verify(x => x.SaveFilePickerAsync(It.IsAny<FilePickerSaveOptions>()), Times.Once);
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task New_project_dialog_can_select_its_location()
        => await VerifyCreationLocationPickerAsync(project: true);

    [AvaloniaTest]
    public async Task New_scene_dialog_can_select_its_location()
        => await VerifyCreationLocationPickerAsync(project: false);

    private static async Task VerifyCreationLocationPickerAsync(bool project)
    {
        await TestReset.ResetShellAsync();
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "picker-folder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var folder = new Mock<IStorageFolder>();
        folder.SetupGet(x => x.Path).Returns(new Uri(path));
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        storage.Setup(x => x.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()))
            .ReturnsAsync(new[] { folder.Object });
        var window = new Window { Width = 800, Height = 600 };
        TestStorageProviderFactory.SetProvider(window, storage.Object);
        FAContentDialog dialog = project
            ? new CreateNewProject { DataContext = new CreateNewProjectViewModel(TestShell.Project, TestShell.VersionControl) }
            : new CreateNewScene { DataContext = new CreateNewSceneViewModel(TestShell.Project, TestShell.Editor) };
        Task? closed = null;
        try
        {
            window.Show();
            closed = dialog.ShowAsync(window);
            HeadlessTestHelpers.Render(3);
            Button button = dialog.GetVisualDescendants().OfType<Button>()
                .Single(x => x.Content is FluentIcon { Icon: Icon.OpenFolder });
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Settle();

            storage.Verify(x => x.OpenFolderPickerAsync(It.IsAny<FolderPickerOpenOptions>()), Times.Once);
            string location = dialog.DataContext is CreateNewProjectViewModel projectViewModel
                ? projectViewModel.Location.Value
                : ((CreateNewSceneViewModel)dialog.DataContext!).Location.Value;
            Assert.That(location, Is.EqualTo(path));
        }
        finally
        {
            dialog.Hide();
            if (closed is not null)
                await closed.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
