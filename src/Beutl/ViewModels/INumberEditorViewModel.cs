using Beutl.Services.Editors;

namespace Beutl.ViewModels;

// Todo: 廃止する
public interface INumberEditorViewModel<T>
    where T : struct
{
    INumberEditorService<T> EditorService { get; }
}
