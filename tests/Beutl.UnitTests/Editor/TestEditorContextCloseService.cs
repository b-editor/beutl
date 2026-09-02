using Beutl.Extensibility;

namespace Beutl.UnitTests.Editor;

internal sealed class TestEditorContextCloseService : IEditorContextCloseService
{
    public static TestEditorContextCloseService Instance { get; } = new();

    public EditorContextCloseRequest RequestClose(IEditorContext context)
        => new(EditorContextCloseRequestStatus.NotOwned, Task.CompletedTask);
}
