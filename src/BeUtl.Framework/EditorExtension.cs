using Avalonia.Controls;
using Avalonia.Media;

namespace BeUtl.Framework;

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract Geometry Icon { get; }

    public abstract string[] FileExtensions { get; }

    public abstract bool TryCreateEditor(string file, out IEditor? editor);

    public abstract bool TryCreatePreviewer(string file, out IControl? previewer);

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

    public abstract bool CanPreview(string file);
}
