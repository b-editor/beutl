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

#pragma warning disable CS8618
    public MenuBarViewModel(ProjectService projectService, EditorService editorService)
    {
        _projectService = projectService;
        _editorService = editorService;
        IsProjectOpened = _projectService.IsOpened;

        IObservable<bool> isSceneOpened = _editorService.SelectedTabItem
            .SelectMany(i => i?.Context ?? Observable.Empty<IEditorContext?>())
            .Select(v => v is EditViewModel);

        Parallel.Invoke(
            () => InitializeFilesCommands(),
            () => InitializeSceneCommands(isSceneOpened),
            () => InitializeViewCommands(isSceneOpened));

        //InitializeFilesCommands();
        //InitializeSceneCommands(isSceneOpened);

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
}
