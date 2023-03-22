namespace Beutl;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ShouldSerializeAttribute : Attribute
{
    public ShouldSerializeAttribute(bool shouldSerialize)
    {
        ShouldSerialize = shouldSerialize;
    }

    public bool ShouldSerialize { get; }
}
