namespace Beutl;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class NotAutoSerializedAttribute : Attribute
{
    public NotAutoSerializedAttribute()
    {
    }
}
