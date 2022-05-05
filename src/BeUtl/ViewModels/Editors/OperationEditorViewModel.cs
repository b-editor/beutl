using BeUtl.Collections;
using BeUtl.ProjectSystem;
using BeUtl.Services;

namespace BeUtl.ViewModels.Editors;

public sealed class OperationEditorViewModel : IDisposable
{
    private readonly IDisposable _disposable0;

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

    public void Dispose()
    {
        _disposable0.Dispose();
        foreach (BaseEditorViewModel? item in Properties.AsSpan())
        {
            item?.Dispose();
        }
    }
}
