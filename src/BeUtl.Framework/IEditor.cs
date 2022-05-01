using Avalonia.Controls;

namespace BeUtl.Framework;

public interface IEditor : IControl
{
    [Obsolete("Use 'IEditorContext.Extension'", true)]
    ViewExtension Extension => throw new NotImplementedException();

    [Obsolete("Use 'IEditorContext.EdittingFile'", true)]
    string EdittingFile => throw new NotImplementedException();

    [Obsolete("Use 'IEditorContext.Commands'", true)]
    IKnownEditorCommands? Commands => null;

    void Close();
}
