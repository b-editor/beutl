using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using FluentAvalonia.UI.Controls;

namespace Beutl.Extensibility;

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract IconSource? GetIcon();

    public abstract bool TryCreateEditor(
        string file,
        [NotNullWhen(true)] out Control? editor);

    // NOTE: ここからProjectItemを取得する場合、
    //       ProjectItemContainerから取得すればいい
    public abstract bool TryCreateContext(
        string file,
        [NotNullWhen(true)] out IEditorContext? context);

    public virtual bool IsSupported(string file)
    {
        return MatchFileExtension(Path.GetExtension(file));
    }

    // extはピリオドを含む
    public abstract bool MatchFileExtension(string ext);
}
