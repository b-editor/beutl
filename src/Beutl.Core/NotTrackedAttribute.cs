namespace Beutl;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class NotTrackedAttribute : Attribute
{
    public NotTrackedAttribute()
    {
    }
}
