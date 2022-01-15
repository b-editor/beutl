using BeUtl.Services.Editors;

namespace BeUtl.ViewModels;

public interface INumberEditorViewModel<T>
    where T : struct
{
    T Maximum { get; }

    T Minimum { get; }

    INumberEditorService<T> EditorService { get; }
}
