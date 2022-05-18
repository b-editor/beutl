using System.Text.Json.Nodes;

using BeUtl.Collections;
using BeUtl.Models;
using BeUtl.ProjectSystem;

namespace BeUtl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable
{
    private readonly IDisposable _disposable;

    public PropertiesEditorViewModel(Layer layer)
    {
        Layer = layer;
        _disposable = layer.Children.ForEachItem(
            (idx, item) => Items.Insert(idx, new OperationEditorViewModel(item)),
            (idx, _) =>
            {
                Items[idx].Dispose();
                Items.RemoveAt(idx);
            },
            () =>
            {
                foreach (OperationEditorViewModel item in Items.AsSpan())
                {
                    item.Dispose();
                }
                Items.Clear();
            });
        RestoreState();
    }

    public Layer Layer { get; }

    public CoreList<OperationEditorViewModel> Items { get; } = new();

    public void Dispose()
    {
        SaveState();
        _disposable.Dispose();
        foreach (OperationEditorViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }
    }

    private string ViewStateDirectory()
    {
        string directory = Path.GetDirectoryName(Layer.FileName)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState()
    {
        string viewStateDir = ViewStateDirectory();
        var json = new JsonArray();
        foreach (OperationEditorViewModel item in Items)
        {
            json.Add(item.SaveState());
        }

        json.JsonSave(Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(Layer.FileName)}.config"));
    }

    private void RestoreState()
    {
        string viewStateDir = ViewStateDirectory();
        string viewStateFile = Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(Layer.FileName)}.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is JsonArray array)
            {
                foreach ((JsonNode item, OperationEditorViewModel op) in array.Zip(Items))
                {
                    op.RestoreState(item);
                }
            }
        }
    }
}
