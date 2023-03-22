namespace Beutl;

public abstract class ProjectItem : Hierarchical, IStorable
{
    public static readonly CoreProperty<string> FileNameProperty;
    private string? _fileName;

    static ProjectItem()
    {
        FileNameProperty = ConfigureProperty<string, ProjectItem>(nameof(FileName))
            .Accessor(o => o.FileName, (o, v) => o.FileName = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();
    }

    public string FileName
    {
        get => _fileName!;
        set => SetAndRaise(FileNameProperty, ref _fileName!, value);
    }

    public DateTime LastSavedTime { get; private set; }

    public event EventHandler? Saved;
    public event EventHandler? Restored;

    public virtual void Restore(string filename)
    {
        FileName = filename;
        RestoreCore(filename);
        LastSavedTime = File.GetLastWriteTimeUtc(filename);

        Restored?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Save(string filename)
    {
        FileName = filename;
        LastSavedTime = DateTime.UtcNow;
        SaveCore(filename);
        File.SetLastWriteTimeUtc(filename, LastSavedTime);

        Saved?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void RestoreCore(string filename)
    {
    }

    protected virtual void SaveCore(string filename)
    {
    }
}
