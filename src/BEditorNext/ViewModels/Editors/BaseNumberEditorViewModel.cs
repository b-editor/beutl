using BEditorNext.ProjectSystem;
using BEditorNext.Services.Editors;

namespace BEditorNext.ViewModels.Editors;

public abstract class BaseNumberEditorViewModel<T> : BaseEditorViewModel<T>
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
