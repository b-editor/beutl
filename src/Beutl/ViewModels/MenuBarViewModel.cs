using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed partial class MenuBarViewModel
{
    private readonly ILogger _logger = Log.CreateLogger<MenuBarViewModel>();

#pragma warning disable CS8618
    public MenuBarViewModel()
    {
        IsProjectOpened = ProjectService.Current.IsOpened;

        IObservable<bool> isProjectOpenedAndTabOpened = ProjectService.Current.IsOpened
            .CombineLatest(EditorService.Current.SelectedTabItem)
            .Select(i => i.First && i.Second != null);

        IObservable<bool> isSceneOpened = EditorService.Current.SelectedTabItem
            .SelectMany(i => i?.Context ?? Observable.Empty<IEditorContext?>())
            .Select(v => v is EditViewModel);

        Parallel.Invoke(
            () => InitializeFilesCommands(),
            () => InitializeSceneCommands(isSceneOpened));

        //InitializeFilesCommands();
        //InitializeSceneCommands(isSceneOpened);

        AddToProject = new(isProjectOpenedAndTabOpened);
        RemoveFromProject = new(isProjectOpenedAndTabOpened);

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

    // Project
    //    Add
    //    Remove
    public ReactiveCommandSlim AddToProject { get; }

    public ReactiveCommandSlim RemoveFromProject { get; }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    private static async Task OnUndo()
    {
        IKnownEditorCommands? commands = EditorService.Current.SelectedTabItem.Value?.Commands.Value;
        if (commands != null)
            await commands.OnUndo();
    }

    private static async Task OnRedo()
    {
        IKnownEditorCommands? commands = EditorService.Current.SelectedTabItem.Value?.Commands.Value;
        if (commands != null)
            await commands.OnRedo();
    }
}
