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
        AutoSplitBySilence = new ReactiveCommandSlim(isSceneOpened);

        Undo = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnUndo);
        Redo = new AsyncReactiveCommand(IsProjectOpened)
            .WithSubscribe(OnRedo);
    }

    public AsyncReactiveCommand Undo { get; }

    public ReactiveCommandSlim AutoSplitBySilence { get; }

    public AsyncReactiveCommand Redo { get; }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    private async Task OnUndo()
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is IUndoRedoEditorContext editor)
            await editor.UndoAsync();
    }

    private async Task OnRedo()
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is IUndoRedoEditorContext editor)
            await editor.RedoAsync();
    }
}
