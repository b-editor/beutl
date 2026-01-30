namespace Beutl.Engine;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class PresenterTypeAttribute(Type presenterType) : Attribute
{
    public Type PresenterType { get; } = presenterType;
}
