using Avalonia;
using Beutl.Api.Services;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// There are no ProjectService/EditorService/ExtensionProvider singletons; MainViewModel owns those
// instances and exposes them as internal properties (visible here via InternalsVisibleTo).
internal static class TestShell
{
    public static MainViewModel MainViewModel => ((TestApp)Application.Current!).GetMainViewModel();

    public static ProjectService Project => MainViewModel.ProjectService;

    public static EditorService Editor => MainViewModel.EditorService;

    public static VersionControlCoordinator VersionControl => MainViewModel.VersionControlCoordinator;

    public static ExtensionProvider Extensions => MainViewModel.ExtensionProvider;

    // Resets the shell, creates a 640x480 project in a new folder under BEUTL_HOME and opens its scene.
    public static async Task<(Project Project, Scene Scene, EditViewModel Editor)> OpenNewSceneAsync(string name)
    {
        await TestReset.ResetShellAsync();
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(location);
        Project project = (await Project.CreateProject(640, 480, 30, 44100, name, location))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (project, scene, (EditViewModel)Editor.SelectedTabItem.Value!.Context.Value);
    }
}
