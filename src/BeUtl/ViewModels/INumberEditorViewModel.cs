using BeUtl.Services.Editors;

namespace BeUtl.ViewModels;

public interface INumberEditorViewModel<T>
    where T : struct
{
    INumberEditorService<T> EditorService { get; }
}
