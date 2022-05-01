
using BeUtl.Collections;
using BeUtl.ProjectSystem;

namespace BeUtl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable
{
    private IDisposable? _disposable;

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
    }

    public Layer Layer { get; }

    public CoreList<OperationEditorViewModel> Items { get; } = new();

    public void Dispose()
    {
        _disposable?.Dispose();
        _disposable = null;
        foreach (OperationEditorViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }
        Items.Clear();
    }
}
