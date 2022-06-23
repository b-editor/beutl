using System.Text.Json.Nodes;

using BeUtl.Collections;
using BeUtl.Framework;
using BeUtl.Models;
using BeUtl.ProjectSystem;
using BeUtl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class OperationsEditorViewModel : IToolContext
{
    private readonly IDisposable _disposable0;
    private IDisposable? _disposable1;
    private Layer? _oldLayer;

    public OperationsEditorViewModel(Scene scene)
    {
        Layer = scene.GetObservable(Scene.SelectedItemProperty)
            .ToReadOnlyReactivePropertySlim();

        Header = StringResources.Common.OperationsObservable.ToReadOnlyReactivePropertySlim()!;

        _disposable0 = Layer.Subscribe(layer =>
        {
            if (_oldLayer != null)
            {
                SaveState(_oldLayer);
            }
            _oldLayer = layer;

            ClearItems();
            if (layer != null)
            {
                _disposable1?.Dispose();
                _disposable1 = layer.Children.ForEachItem(
                    (idx, item) => Items.Insert(idx, new OperationEditorViewModel(item)),
                    (idx, _) =>
                    {
                        Items[idx].Dispose();
                        Items.RemoveAt(idx);
                    },
                    () => ClearItems());

                RestoreState(layer);
            }
        });
    }

    public ReadOnlyReactivePropertySlim<Layer?> Layer { get; }

    public CoreList<OperationEditorViewModel> Items { get; } = new();

    public ToolTabExtension Extension => OperationsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void Dispose()
    {
        if (Layer.Value != null)
        {
            SaveState(Layer.Value);
        }
        _disposable0.Dispose();
        _disposable1?.Dispose();
        ClearItems();

        Layer.Dispose();
        Header.Dispose();
    }

    private string ViewStateDirectory(Layer layer)
    {
        string directory = Path.GetDirectoryName(layer.FileName)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState(Layer layer)
    {
        string viewStateDir = ViewStateDirectory(layer);
        var json = new JsonArray();
        foreach (OperationEditorViewModel item in Items)
        {
            json.Add(item.SaveState());
        }

        json.JsonSave(Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(layer.FileName)}.config"));
    }

    private void RestoreState(Layer layer)
    {
        string viewStateDir = ViewStateDirectory(layer);
        string viewStateFile = Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(layer.FileName)}.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is JsonArray array)
            {
                foreach ((JsonNode? item, OperationEditorViewModel op) in array.Zip(Items))
                {
                    if (item != null)
                    {
                        op.RestoreState(item);
                    }
                }
            }
        }
    }

    private void ClearItems()
    {
        foreach (OperationEditorViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }
        Items.Clear();
    }
}
