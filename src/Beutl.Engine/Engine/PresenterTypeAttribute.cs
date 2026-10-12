using System;
using System.Reflection;

namespace Beutl.Engine;

/// <summary>Specifies the presenter type associated with the annotated type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class PresenterTypeAttribute(Type presenterType) : Attribute
{
    public Type PresenterType { get; } = presenterType;

    /// <returns>The presenter type declared on <paramref name="targetType"/>, or <c>null</c> when there is none.</returns>
    public static Type? GetPresenterType(Type targetType)
    {
        if (targetType == null)
            throw new ArgumentNullException(nameof(targetType));

        PresenterTypeAttribute? attribute =
            targetType.GetCustomAttribute<PresenterTypeAttribute>(inherit: true);

        return attribute?.PresenterType;
    }
}
