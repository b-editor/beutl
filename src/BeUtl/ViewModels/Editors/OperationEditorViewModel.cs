using BeUtl.Collections;
using BeUtl.ProjectSystem;
using BeUtl.Services;

namespace BeUtl.ViewModels.Editors;

public class OperationEditorViewModel : IDisposable
{
    private IDisposable? _disposable0;

    public OperationEditorViewModel(LayerOperation model)
    {
        Model = model;
        _disposable0 = model.Properties.ForEachItem(
            (idx, item) => Properties.Insert(idx, PropertyEditorService.CreateEditorViewModel(item)),
            (idx, _) =>
            {
                Properties[idx]?.Dispose();
                Properties.RemoveAt(idx);
            },
            () =>
            {
                foreach (BaseEditorViewModel? item in Properties.AsSpan())
                {
                    item?.Dispose();
                }
                Properties.Clear();
            });
    }

    public LayerOperation Model { get; }

    public CoreList<BaseEditorViewModel?> Properties { get; } = new();

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _disposable0?.Dispose();
        _disposable0 = null;
        foreach (BaseEditorViewModel? item in Properties.AsSpan())
        {
            item?.Dispose();
        }
        Properties.Clear();
    }
}
