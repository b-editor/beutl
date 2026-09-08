using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed partial class MenuBarViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<MenuBarViewModel>();
    private readonly ProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly IProjectVersionControlSession _versionControlSession;

#pragma warning disable CS8618
    public MenuBarViewModel(ProjectService projectService, EditorService editorService)
        : this(projectService, editorService, NoProjectVersionControlSession.Instance)
    {
    }

    internal MenuBarViewModel(
        ProjectService projectService,
        EditorService editorService,
        IProjectVersionControlSession versionControlSession)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _versionControlSession = versionControlSession
            ?? throw new ArgumentNullException(nameof(versionControlSession));
        IsProjectOpened = _projectService.IsOpened;

        IObservable<bool> isSceneOpened = _editorService.SelectedTabItem
            .Select(item => item?.Context.Select(context => context is EditViewModel)
                ?? Observable.Return(false))
            .Switch()
            .DistinctUntilChanged();

        InitializeFilesCommands();
        InitializeSceneCommands(isSceneOpened);
        InitializeViewCommands(isSceneOpened);
        InitializeAiCommands(isSceneOpened);

        Undo = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnUndo);
        Redo = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnRedo);
    }

    // Edit
    //    Undo
    //    Redo
    public AsyncReactiveCommand Undo { get; }

    public AsyncReactiveCommand Redo { get; }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    private async Task OnUndo()
    {
        IKnownEditorCommands? commands = _editorService.SelectedTabItem.Value?.Commands.Value;
        if (commands != null)
            await commands.OnUndo();
    }

    private async Task OnRedo()
    {
        IKnownEditorCommands? commands = _editorService.SelectedTabItem.Value?.Commands.Value;
        if (commands != null)
            await commands.OnRedo();
    }

    private sealed class NoProjectVersionControlSession : IProjectVersionControlSession
    {
        public static NoProjectVersionControlSession Instance { get; } = new();

        private NoProjectVersionControlSession()
        {
        }

        public IReadOnlyReactiveProperty<bool> IsGitAvailable { get; }
            = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<bool> IsTracked { get; }
            = new ReactivePropertySlim<bool>();

        public Task NotifySavedAsync(
            IProjectFileWriteLease? completedWrite = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
