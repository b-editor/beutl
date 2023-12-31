using System.Diagnostics.CodeAnalysis;

namespace Beutl;

public sealed class ProjectItemContainer
{
    private readonly List<WeakReference<ProjectItem>> _items = [];
    private readonly BeutlApplication _app = BeutlApplication.Current;
    private IProjectItemGenerator? _generator;

    public static ProjectItemContainer Current { get; } = new();

    public IProjectItemGenerator Generator
    {
        get => _generator!;
        internal set => _generator ??= value;
    }

    public bool IsCreated(string file)
    {
        return _items.Any(i => i.TryGetTarget(out ProjectItem? target) && target.FileName == file);
    }

    public bool Remove(string file)
    {
        WeakReference<ProjectItem>? item
            = _items.Find(i => i.TryGetTarget(out ProjectItem? target) && target.FileName == file);
        if (item != null)
            return _items.Remove(item);
        else
            return false;
    }

    public bool TryGetOrCreateItem<T>(string file, [NotNullWhen(true)] out T? item)
        where T : ProjectItem
    {
        item = default;
        item = _items.Find(i => i.TryGetTarget(out ProjectItem? target) && target.FileName == file && target is T)
            ?.TryGetTarget(out ProjectItem? target) ?? false
            ? target as T
            : null;

        if (item != null)
        {
            // ファイルの最後に書き込まれた時間が
            // item.LastSavedTimeよりも新しい場合、復元する。
            if (item.LastSavedTime.ToUniversalTime() < File.GetLastWriteTimeUtc(file))
            {
                item.Restore(file);
            }

            return true;
        }

        if (Generator.TryCreateItem(file, out T? typed))
        {
            Add(typed);
            item = typed;
            return true;
        }

        return false;
    }

    public bool TryGetOrCreateItem(string file, [NotNullWhen(true)] out ProjectItem? item)
    {
        item = default;
        item = _items.Find(i => i.TryGetTarget(out ProjectItem? target) && target.FileName == file)
            ?.TryGetTarget(out ProjectItem? target) ?? false
            ? target
            : default;
        if (item != null)
        {
            // ファイルの最後に書き込まれた時間が
            // item.LastSavedTimeよりも新しい場合、復元する。
            if (item.LastSavedTime.ToUniversalTime() < File.GetLastWriteTimeUtc(file))
            {
                item.Restore(file);
            }

            return true;
        }

        if (Generator.TryCreateItem(file, out ProjectItem? result))
        {
            Add(result);
            item = result;
            return true;
        }

        return false;
    }

    public void Add(ProjectItem item)
    {
        _app.Items.Add(item);
        foreach (WeakReference<ProjectItem> wref in _items)
        {
            if (!wref.TryGetTarget(out _))
            {
                wref.SetTarget(item);
                return;
            }
        }

        _items.Add(new WeakReference<ProjectItem>(item));
    }
}
