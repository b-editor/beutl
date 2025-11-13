namespace Beutl.Protocol.Operations;

/// <summary>
/// Represents an operation that provides access to a PropertyPath.
/// </summary>
public interface IPropertyPathProvider
{
    /// <summary>
    /// Gets the property path that this operation affects.
    /// </summary>
    string PropertyPath { get; }
}
