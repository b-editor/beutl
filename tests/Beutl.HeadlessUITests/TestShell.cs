using Avalonia;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
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

    public static Beutl.Services.LibraryService Library => ((TestApp)Application.Current!).Library;

    public static Beutl.Configuration.GlobalConfiguration Configuration => ((TestApp)Application.Current!).Configuration;

    public static DecoderRegistry Decoders => ((TestApp)Application.Current!).Decoders;

    public static Beutl.Editor.Components.FileBrowserTab.Services.FileThumbnailService Thumbnails => (Beutl.Editor.Components.FileBrowserTab.Services.FileThumbnailService)MainViewModel.AppServices.GetService(typeof(Beutl.Editor.Components.FileBrowserTab.Services.FileThumbnailService))!;

    public static Beutl.Media.FontManager FontManager => ((TestApp)Application.Current!).Fonts;

    public static NotificationCapture Notifications => ((TestApp)Application.Current!).Notifications;

    public static IExtensionContext ExtensionContext => ((TestApp)Application.Current!).ExtensionContext;

    public static ObjectTemplateService Templates => ((TestApp)Application.Current!).Templates;

    public static ProjectService Project => MainViewModel.ProjectService;

    public static EditorService Editor => MainViewModel.EditorService;

    public static VersionControlCoordinator VersionControl => MainViewModel.VersionControlCoordinator;

    public static ExtensionProvider Extensions => MainViewModel.ExtensionProvider;

    public static void EnsureDecoder<T>() where T : IDecoderInfo, new()
    {
        var decoders = Decoders;
        lock (decoders.RegistrationLock)
        {
            if (!decoders.EnumerateDecoder().OfType<T>().Any())
                decoders.Register(new T());
        }
    }

    public static EditorServiceRegistry CreateEditorServices() => new EditorServiceRegistry()
        .Add<INotificationService>(() => Notifications)
        .Add(() => FontManager)
        .Add(() => Decoders);

    public static Moq.Mock<IEditorContext> CreateEditorContextMock()
    {
        var context = new Moq.Mock<IEditorContext>();
        context.Setup(x => x.GetService(typeof(INotificationService))).Returns(Notifications);
        context.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(Configuration);
        context.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(FontManager);
        context.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
        context.Setup(x => x.GetService(typeof(Beutl.Services.LibraryService))).Returns(Library);
        context.Setup(x => x.GetService(typeof(ObjectTemplateService))).Returns(Templates);
        context.Setup(x => x.GetService(typeof(Beutl.Editor.Components.FileBrowserTab.Services.FileThumbnailService))).Returns(Thumbnails);
        return context;
    }

    public static async Task WaitForProjectWritesAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        using (await Editor.BeginProjectFileWriteAsync(CancellationToken.None)) { }
    }

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
        Project project = (await Project.CreateProject(width, height, new Rational(30), 44100, name, NewWorkspace(name)))!;
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
        MediaTime? duration = null, bool reset = true)
    {
        if (reset)
            await TestReset.ResetShellAsync();
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(location);
        Project project = (await Project.CreateProject(width, height, new Rational(frameRate), sampleRate, name, location))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        if (duration is { } length)
            scene.Duration.Value = length;
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
        EditViewModel editor, Func<EngineObject> create, MediaTime start, MediaTime length, int layer = 0)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementAddResult result = await adder.AddAsync(
            [new ElementDescription(start, length, layer, new ElementSource.EngineObject(create))],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        return result;
    }
}
