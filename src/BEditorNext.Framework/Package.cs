namespace BEditorNext.Framework;

public abstract class Package
{
    public abstract PackageInfo Info { get; }

    public abstract IEnumerable<Extension> GetExtensions();
}
