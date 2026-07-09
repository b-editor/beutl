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
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(DeleteLayer), nameof(ExcludeLayer), nameof(CutLayer), nameof(PasteLayer), nameof(CopyLayer), nameof(ShowSceneSettings), nameof(RemoveFromProject))]
    private void InitializeSceneCommands(IObservable<bool> isSceneOpened)
    {
        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(_editorService.SelectedTabItem)
            .Select(i => i is { First: true, Second: not null });

        RemoveFromProject = new(isProjectOpenedAndTabOpened);

        DeleteLayer = new(isSceneOpened);
        ExcludeLayer = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnExcludeElement);

        CutLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnCutElement);
        CopyLayer = new AsyncReactiveCommand(isSceneOpened)
            .WithSubscribe(OnCopyElement);
        PasteLayer = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(() =>
            {
                if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
                    && viewModel.FindToolTab<TimelineTabViewModel>() is TimelineTabViewModel timeline)
                {
                    timeline.Paste.Execute();
                }
            });

        ShowSceneSettings = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnShowSceneSettings);
    }

    // Scene
    //    New
    //    Remove
    //    Settings
    //    Layer
    //       Add
    //       Delete
    //       Exclude
    //       Cut
    //       Copy
    //       Paste
    public ReactiveCommandSlim NewScene { get; } = new();

    public ReactiveCommandSlim<EditorTabItem?> RemoveFromProject { get; private set; }

    public ReactiveCommandSlim DeleteLayer { get; private set; }

    public ReactiveCommandSlim ExcludeLayer { get; private set; }

    public AsyncReactiveCommand CutLayer { get; private set; }

    public AsyncReactiveCommand CopyLayer { get; private set; }

    public ReactiveCommandSlim PasteLayer { get; private set; }

    public ReactiveCommandSlim ShowSceneSettings { get; private set; }

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

    private void OnExcludeElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.GetService<IEditorSelection>()?.SelectedObject.Value is Element element)
        {
            viewModel.GetRequiredService<IElementStructureService>()
                .Exclude(scene, [element], GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled);
        }
    }

    private async Task OnCutElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.GetService<IEditorSelection>()?.SelectedObject.Value is Element element)
        {
            await viewModel.GetRequiredService<IElementClipboardService>()
                .CutAsync(scene, [element], GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled);
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
