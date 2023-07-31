namespace Beutl;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class NotifiableAttribute : Attribute
{
    public NotifiableAttribute(bool notifiable)
    {
        Notifiable = notifiable;
    }

    public bool Notifiable { get; }
}
