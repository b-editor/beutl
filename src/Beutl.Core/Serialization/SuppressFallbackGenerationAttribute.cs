namespace Beutl.Serialization;

/// <summary>
/// Disables generated <see cref="IFallback"/> members for a custom implementation.
/// The annotated type is responsible for retaining its payload in Serialize and Deserialize.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SuppressFallbackGenerationAttribute : Attribute;
