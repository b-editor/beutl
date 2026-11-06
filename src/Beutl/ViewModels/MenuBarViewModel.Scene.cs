using System.Diagnostics.CodeAnalysis;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(NewScene), nameof(DeleteLayer), nameof(CutLayer), nameof(PasteLayer), nameof(CopyLayer), nameof(SelectAll), nameof(ShowSceneSettings), nameof(RemoveFromProject))]
    private void InitializeSceneCommands(IObservable<bool> isSceneOpened)
    {
        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(_editorService.SelectedTabItem)
            .Select(i => i is { First: true, Second: not null });

        // A scene belongs to a project.
        NewScene = new AsyncReactiveCommand(IsProjectOpened);

        RemoveFromProject = new(isProjectOpenedAndTabOpened);

        DeleteLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnDeleteElement);

        CutLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnCutElement);
        CopyLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnCopyElement);
        PasteLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnPasteElement);
        SelectAll = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnSelectAll);

        ShowSceneSettings = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnShowSceneSettings);
        RelinkMedia = new AsyncReactiveCommand(isSceneOpened).WithSubscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out var editor))
                await editor.OpenMissingMediaAsync(refresh: true);
        });
    }

    public AsyncReactiveCommand NewScene { get; private set; }

    public ReactiveCommandSlim<EditorTabItem?> RemoveFromProject { get; private set; }

    public AsyncReactiveCommand DeleteLayer { get; private set; }

    public AsyncReactiveCommand CutLayer { get; private set; }

    public AsyncReactiveCommand CopyLayer { get; private set; }

    public AsyncReactiveCommand PasteLayer { get; private set; }

    public ReactiveCommandSlim SelectAll { get; private set; }

    public ReactiveCommandSlim ShowSceneSettings { get; private set; }

    public AsyncReactiveCommand RelinkMedia { get; private set; }

    private bool TryGetSelectedEditViewModel([NotNullWhen(true)] out EditViewModel? viewModel)
    {
        if (_editorService.SelectedTabItem.Value?.Context.Value is EditViewModel editViewModel)
        {
            viewModel = editViewModel;
            return true;
        }
        else
        {
            viewModel = null;
            return false;
        }
    }

    private Task OnDeleteElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.GetService<IEditorSelection>() is { } selection)
        {
            viewModel.GetRequiredService<IElementStructureService>().Delete(
                scene, selection.GetTargetElements(scene), GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value);
        }

        return Task.CompletedTask;
    }

    private async Task OnCutElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.GetService<IEditorSelection>() is { } selection)
        {
            await viewModel.GetRequiredService<IElementClipboardService>().CutAsync(
                scene, selection.GetTargetElements(scene), GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value);
        }
    }

    private async Task OnCopyElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.GetService<IEditorSelection>() is { } selection)
        {
            await viewModel.GetRequiredService<IElementClipboardService>().CopyAsync(selection.GetTargetElements(scene));
        }
    }

    private async Task OnPasteElement()
    {
        if (!TryGetSelectedEditViewModel(out EditViewModel? viewModel)) return;

        // An open timeline pastes where it was last clicked and scrolls to the pasted clip.
        if (viewModel.FindToolTab<TimelineTabViewModel>() is { } timeline)
        {
            await timeline.Paste.ExecuteAsync();
            return;
        }

        Scene scene = viewModel.Scene;
        (MediaTime start, int layer) = PlayheadPlacement.Find(viewModel, scene);
        try
        {
            ElementPasteOutcome outcome = await viewModel.GetRequiredService<IElementClipboardService>()
                .PasteAsync(scene, start, layer);
            if (outcome.AddFailure is { } failure)
            {
                TimelineTabViewModel.ShowPasteFailure(failure);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while pasting an element.");
            NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
        }
    }

    private void OnSelectAll()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
            viewModel.GetService<IEditorSelection>()?.SelectAll(viewModel.Scene);
    }

    private void OnShowSceneSettings()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel))
        {
            SceneSettingsTabViewModel? tab = viewModel.FindToolTab<SceneSettingsTabViewModel>();
            if (tab != null)
            {
                tab.IsSelected.Value = true;
            }
            else
            {
                viewModel.OpenToolTab(new SceneSettingsTabViewModel(viewModel));
            }
        }
    }
}
