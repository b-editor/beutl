namespace Beutl.Engine;

public class EngineObject : Hierarchical
{
    public virtual IReadOnlyList<IProperty> Properties => throw null!;

    internal int Version { get; private set; }

    protected void ScanProperties<T>() where T : EngineObject
    {
        throw null!;
    }
}
