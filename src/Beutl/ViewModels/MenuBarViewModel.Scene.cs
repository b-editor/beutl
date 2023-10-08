using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia.Input;
using Avalonia.Input.Platform;

using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels.Tools;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(AddLayer), nameof(DeleteLayer), nameof(ExcludeLayer), nameof(CutLayer), nameof(PasteLayer), nameof(CopyLayer), nameof(ShowSceneSettings))]
    private void InitializeSceneCommands(IObservable<bool> isSceneOpened)
    {
        AddLayer = new(isSceneOpened);
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
                    && viewModel.FindToolTab<TimelineViewModel>() is TimelineViewModel timeline)
                {
                    timeline.Paste.Execute();
                }
            });

        ShowSceneSettings = new ReactiveCommandSlim(isSceneOpened)
            .WithSubscribe(OnShowSceneSettings);
    }

    // Scene
    //    New
    //    Settings
    //    Layer
    //       Add
    //       Delete
    //       Exclude
    //       Cut
    //       Copy
    //       Paste
    public ReactiveCommandSlim NewScene { get; } = new();

    public ReactiveCommandSlim AddLayer { get; private set; }

    public ReactiveCommandSlim DeleteLayer { get; private set; }

    public ReactiveCommandSlim ExcludeLayer { get; private set; }

    public AsyncReactiveCommand CutLayer { get; private set; }

    public AsyncReactiveCommand CopyLayer { get; private set; }

    public ReactiveCommandSlim PasteLayer { get; private set; }

    public ReactiveCommandSlim ShowSceneSettings { get; private set; }

    private static bool TryGetSelectedEditViewModel([NotNullWhen(true)] out EditViewModel? viewModel)
    {
        if (EditorService.Current.SelectedTabItem.Value?.Context.Value is EditViewModel editViewModel)
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

    private static DataObject CreateElementDataObject(Element element)
    {
        var jsonNode = new JsonObject();
        element.WriteToJson(jsonNode);
        string json = jsonNode.ToJsonString(JsonHelper.SerializerOptions);
        var data = new DataObject();
        data.Set(DataFormats.Text, json);
        data.Set(Constants.Element, json);

        return data;
    }

    private void OnExcludeElement()
    {
        if (TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.SelectedObject.Value is Element element)
        {
            scene.RemoveChild(element).DoAndRecord(CommandRecorder.Default);
        }
    }

    private async Task OnCutElement()
    {
        if (App.GetClipboard() is IClipboard clipboard
            && TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.Scene is Scene scene
            && viewModel.SelectedObject.Value is Element element)
        {
            DataObject data = CreateElementDataObject(element);

            await clipboard.SetDataObjectAsync(data);
            scene.RemoveChild(element).DoAndRecord(CommandRecorder.Default);
        }
    }

    private async Task OnCopyElement()
    {
        if (App.GetClipboard() is IClipboard clipboard
            && TryGetSelectedEditViewModel(out EditViewModel? viewModel)
            && viewModel.SelectedObject.Value is Element element)
        {
            DataObject data = CreateElementDataObject(element);

            await clipboard.SetDataObjectAsync(data);
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
