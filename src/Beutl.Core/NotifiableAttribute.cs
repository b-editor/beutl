namespace Beutl;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class NotifiableAttribute(bool notifiable) : Attribute
{
    public bool Notifiable { get; } = notifiable;
}
