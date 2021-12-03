using Avalonia.Controls;
using Avalonia.Media;

namespace BEditorNext.Framework;

public record PackageInfo
{
    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public Guid Id { get; init; }

    public PackageLicense License { get; init; }

    public string Description { get; init; } = string.Empty;

    public IList<string> Tags { get; init; } = new List<string>();
}

public abstract class Package
{
    public abstract PackageInfo Info { get; }

    public abstract IEnumerable<Extension> GetExtensions();
}

// 拡張機能の基本クラス
public abstract class Extension
{
    public abstract string Name { get; }

    public abstract string DisplayName { get; }
}

// UIの拡張機能の基本クラス
public abstract class ViewExtension : Extension
{
}

public interface IEditor : IControl, IAsyncDisposable
{
}

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
