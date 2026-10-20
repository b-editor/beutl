using Avalonia;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
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

    // A new folder under BEUTL_HOME.
    public static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    // Creates a project named after its NewWorkspace folder.
    public static async Task<Project> CreateProjectAsync(string name, int width = 640, int height = 480)
    {
        Project project = (await Project.CreateProject(width, height, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        return project;
    }

    // Creates a project with CreateProjectAsync and opens its scene, without resetting the shell first.
    public static async Task<EditViewModel> OpenNewSceneEditorAsync(string name, int width = 640, int height = 480)
    {
        Project project = await CreateProjectAsync(name, width, height);
        Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)Editor.SelectedTabItem.Value!.Context.Value;
    }

    // Resets the shell unless reset is false, creates a project in a new folder under BEUTL_HOME and opens
    // its scene, after giving the scene the duration when one is given.
    public static async Task<(Project Project, Scene Scene, EditViewModel Editor)> OpenNewSceneAsync(
        string name, int width = 640, int height = 480, int frameRate = 30, int sampleRate = 44100,
        TimeSpan? duration = null, bool reset = true)
    {
        if (reset)
            await TestReset.ResetShellAsync();
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(location);
        Project project = (await Project.CreateProject(width, height, frameRate, sampleRate, name, location))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        if (duration is { } length)
            scene.Duration = length;
        Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (project, scene, (EditViewModel)Editor.SelectedTabItem.Value!.Context.Value);
    }

    // Resets the shell, opens the project at projectPath and opens its scene.
    public static async Task<EditViewModel> ReopenAsync(string projectPath)
    {
        await TestReset.ResetShellAsync();
        await Project.OpenProject(projectPath);
        Editor.ActivateTabItem(Project.CurrentProject.Value!.Items.OfType<Scene>().Single());
        return (EditViewModel)Editor.SelectedTabItem.Value!.Context.Value;
    }

    // The folder that holds the editor's scene file.
    public static string ProjectRoot(EditViewModel editor) => Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;

    // Adds an element that holds what create returns to the editor's scene.
    public static async Task<ElementAddResult> AddElementAsync(
        EditViewModel editor, Func<EngineObject> create, TimeSpan start, TimeSpan length, int layer = 0)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementAddResult result = await adder.AddAsync(
            [new ElementDescription(start, length, layer, new ElementSource.EngineObject(create))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        return result;
    }
}
