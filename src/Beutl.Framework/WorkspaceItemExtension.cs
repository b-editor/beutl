using System.Diagnostics.CodeAnalysis;

using Avalonia.Platform.Storage;

using FluentAvalonia.UI.Controls;

namespace Beutl.Framework;

// NOTE: EditorExtension内にIWorkspaceItemを作成するメソッドを追加すると、
//       WorkspaceItemのエディターを固定してしまい拡張性が悪くなる（例えばSceneのエディターを変更できないなど）ので、
//       WorkspaceItemExtensisonとEditorExtensionを分けた
public abstract class WorkspaceItemExtension : Extension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract IconSource? GetIcon();

    public abstract bool TryCreateItem(
        string file,
        [NotNullWhen(true)] out IWorkspaceItem? context);

    public abstract bool IsSupported(string file);
}
