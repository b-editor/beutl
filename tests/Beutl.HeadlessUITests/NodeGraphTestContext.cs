using Beutl.Editor.Services;
using Beutl.Extensibility;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// The editor context that a NodeGraphViewModel asks for its services.
internal static class NodeGraphTestContext
{
    // A context with a property editor factory, the mutation service when one is given, and a stopped preview
    // player when player is true.
    public static Mock<IEditorContext> Create(INodeGraphMutationService? mutation = null, bool player = false)
    {
        var editor = new Mock<IEditorContext>();
        editor.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        if (mutation is not null)
            editor.Setup(x => x.GetService(typeof(INodeGraphMutationService))).Returns(mutation);
        if (player)
        {
            var preview = new Mock<IPreviewPlayer>();
            preview.SetupGet(x => x.IsPlaying).Returns(new ReactivePropertySlim<bool>(false));
            editor.Setup(x => x.GetService(typeof(IPreviewPlayer))).Returns(preview.Object);
        }
        return editor;
    }
}
