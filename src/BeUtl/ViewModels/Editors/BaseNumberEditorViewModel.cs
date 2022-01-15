using BeUtl.ProjectSystem;
using BeUtl.Services.Editors;

namespace BeUtl.ViewModels.Editors;

public abstract class BaseNumberEditorViewModel<T> : BaseEditorViewModel<T>, INumberEditorViewModel<T>
    where T : struct
{
    protected BaseNumberEditorViewModel(Setter<T> setter)
        : base(setter)
    {
    }

    public abstract T Maximum { get; }

    public abstract T Minimum { get; }

    public abstract INumberEditorService<T> EditorService { get; }
}
