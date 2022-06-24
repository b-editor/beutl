using BeUtl.Services.Editors;
using BeUtl.Services.Editors.Wrappers;

namespace BeUtl.ViewModels.Editors;

public abstract class BaseNumberEditorViewModel<T> : BaseEditorViewModel<T>, INumberEditorViewModel<T>
    where T : struct
{
    protected BaseNumberEditorViewModel(IWrappedProperty<T> property)
        : base(property)
    {
    }

    public abstract T Maximum { get; }

    public abstract T Minimum { get; }

    public abstract INumberEditorService<T> EditorService { get; }

    public override EditorViewModelDescription Description => base.Description with { NumberEditorService = EditorService };
}
