using BEditorNext.Services.Editors;

namespace BEditorNext.ViewModels;

public interface INumberEditorViewModel<T>
    where T : struct
{
    T Maximum { get; }

    T Minimum { get; }

    INumberEditorService<T> EditorService { get; }
}
