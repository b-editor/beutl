using System;
using System.Reflection;

namespace Beutl.Engine;

/// <summary>
/// Specifies the presenter type associated with a given target type.
/// </summary>
/// <remarks>
/// This attribute can be used to enable automatic presenter resolution
/// based on the annotated object type. Use <see cref="GetPresenterType"/>
/// to retrieve the presenter type for a given target type via reflection.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class PresenterTypeAttribute(Type presenterType) : Attribute
{
    /// <summary>
    /// Gets the presenter <see cref="Type"/> declared for the annotated type.
    /// </summary>
    public Type PresenterType { get; } = presenterType;

    /// <summary>
    /// Resolves the presenter type associated with the specified target type
    /// by reading its <see cref="PresenterTypeAttribute"/>, if present.
    /// </summary>
    /// <param name="targetType">The type for which to resolve a presenter.</param>
    /// <returns>
    /// The resolved presenter <see cref="Type"/> if the attribute is applied;
    /// otherwise, <c>null</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="targetType"/> is <c>null</c>.
    /// </exception>
    public static Type? GetPresenterType(Type targetType)
    {
        if (targetType == null)
            throw new ArgumentNullException(nameof(targetType));

        PresenterTypeAttribute? attribute =
            targetType.GetCustomAttribute<PresenterTypeAttribute>(inherit: true);

        return attribute?.PresenterType;
    }
}
