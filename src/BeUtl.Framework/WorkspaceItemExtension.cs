using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

namespace Beutl.Framework;

// NOTE: EditorExtension内にIWorkspaceItemを作成するメソッドを追加すると、
//       WorkspaceItemのエディターを固定してしまい拡張性が悪くなる（例えばSceneのエディターを変更できないなど）ので、
//       WorkspaceItemExtensisonとEditorExtensionを分けた
public abstract class WorkspaceItemExtension : Extension
{
    public abstract Geometry? Icon { get; }

    public abstract string[] FileExtensions { get; }

    public abstract IObservable<string> FileTypeName { get; }

    public abstract bool TryCreateItem(
        string file,
        [NotNullWhen(true)] out IWorkspaceItem? context);

    public virtual bool IsSupported(string file)
    {
        string ext = Path.GetExtension(file);
        for (int i = 0; i < FileExtensions.Length; i++)
        {
            string item = FileExtensions[i];
            string includePeriod = item.StartsWith('.') ? item : $".{item}";

            if (includePeriod == ext)
            {
                return true;
            }
        }

        return false;
    }
}
