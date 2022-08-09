using BeUtl.Services.Editors;

namespace BeUtl.ViewModels;

// Todo: 廃止する
public interface INumberEditorViewModel<T>
    where T : struct
{
    INumberEditorService<T> EditorService { get; }
}
