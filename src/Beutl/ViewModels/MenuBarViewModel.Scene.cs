using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(DeleteLayer), nameof(CutLayer), nameof(PasteLayer), nameof(CopyLayer), nameof(ShowSceneSettings), nameof(RemoveFromProject))]
    private void InitializeSceneCommands(IObservable<bool> isSceneOpened)
    {
        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(_editorService.SelectedTabItem)
            .Select(i => i is { First: true, Second: not null });

        RemoveFromProject = new(isProjectOpenedAndTabOpened);

        DeleteLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnDeleteElement);

        CutLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnCutElement);
        CopyLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnCopyElement);
        PasteLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnPasteElement);

        ShowSceneSettings = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnShowSceneSettings);
        RelinkMedia = new AsyncReactiveCommand(isSceneOpened).WithSubscribe(async () =>
        {
            if (TryGetSelectedEditViewModel(out var editor))
                await editor.OpenMissingMediaAsync(refresh: true);
        });
    }

    public AsyncReactiveCommand NewScene { get; } = new();

    public ReactiveCommandSlim<EditorTabItem?> RemoveFromProject { get; private set; }

    public AsyncReactiveCommand DeleteLayer { get; private set; }

    public AsyncReactiveCommand CutLayer { get; private set; }

    public AsyncReactiveCommand CopyLayer { get; private set; }

    public AsyncReactiveCommand PasteLayer { get; private set; }

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

    private static DataTransfer CreateElementDataObject(Element element)
    {
        string json = CoreSerializer.SerializeToJsonString(element);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(json));
        data.Add(DataTransferItem.Create(BeutlDataFormats.Element, json));

        return data;
    }

    private bool TryGetSelectedElement(
        [NotNullWhen(true)] out EditViewModel? viewModel,
        [NotNullWhen(true)] out Scene? scene,
        [NotNullWhen(true)] out Element? element)
    {
        if (TryGetSelectedEditViewModel(out viewModel)
            && viewModel.Scene is Scene selectedScene
            && viewModel.GetService<IEditorSelection>()?.SelectedObject.Value is Element selectedElement)
        {
            scene = selectedScene;
            element = selectedElement;
            return true;
        }

        scene = null;
        element = null;
        return false;
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
        if (TryGetSelectedElement(out EditViewModel? viewModel, out Scene? scene, out Element? element))
        {
            await viewModel.GetRequiredService<IElementClipboardService>()
                .CutAsync(scene, [element], GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value);
        }
    }

    private async Task OnCopyElement()
    {
        if (ClipboardHelper.GetClipboard() is IClipboard clipboard
            && TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.GetService<IEditorSelection>()?.SelectedObject.Value is Element element)
        {
            DataTransfer data = CreateElementDataObject(element);

            await clipboard.SetDataAsync(data);
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
            if (outcome.AddFailure is LockedElementLayerFailure)
            {
                NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
            }
            else if (outcome.AddFailure is not null)
            {
                NotificationService.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while pasting an element.");
            NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
        }
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
