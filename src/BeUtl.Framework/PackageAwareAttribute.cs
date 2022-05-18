namespace BeUtl.Framework;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PackageAwareAttribute : Attribute
{
    public PackageAwareAttribute(Type packageType)
    {
        if (!packageType.IsAssignableTo(typeof(Package)))
        {
            throw new InvalidOperationException(
                $"The '{nameof(packageType)}' must be a type derived from the '{typeof(Package)}' class.");
        }

        PackageType = packageType;
    }

    public Type PackageType { get; }
}
