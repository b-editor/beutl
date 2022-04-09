using System.Globalization;

namespace BeUtl.Framework;

public abstract class Package
{
    internal static int s_nextId;
    internal readonly int _id;
    private PackageInfo? _info;

    protected Package()
    {
        _id = s_nextId++;
    }

    public PackageInfo Info
    {
        get => _info ?? throw new InvalidOperationException();
        internal set => _info = value;
    }

    public abstract IEnumerable<Extension> GetExtensions();

    public virtual Avalonia.Controls.IResourceProvider? GetResource(CultureInfo ci)
    {
        return null;
    }
}
