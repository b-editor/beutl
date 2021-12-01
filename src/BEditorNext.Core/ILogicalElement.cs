namespace BEditorNext;

/// <summary>
/// Represents a element in the logical tree.
/// </summary>
public interface ILogicalElement
{
    /// <summary>
    /// Gets the logical parent.
    /// </summary>
    ILogicalElement? LogicalParent { get; }

    /// <summary>
    /// Gets the logical children.
    /// </summary>
    IEnumerable<ILogicalElement> LogicalChildren { get; }
}
