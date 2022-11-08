using Avalonia.Controls;

namespace Beutl.Framework;

public interface IEditor : IControl
{
    [Obsolete("Use 'IEditorContext.Extension'", true)]
    ViewExtension Extension => throw new NotImplementedException();

    [Obsolete("Use 'IEditorContext.EdittingFile'", true)]
    string EdittingFile => throw new NotImplementedException();

    [Obsolete("Use 'IEditorContext.Commands'", true)]
    IKnownEditorCommands? Commands => null;

    [Obsolete("Use 'IEditorContext.Dispose'", true)]
    void Close()
    {
    }
}
