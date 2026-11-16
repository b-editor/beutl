using System.Text.Json.Serialization;

namespace Beutl.NodeGraph;

/// <summary>The dimensions a user has chosen for a node in the graph editor, in device-independent pixels.</summary>
/// <param name="Width">The node width, or null to use its type's <see cref="GraphNode.EditorWidth"/>.</param>
/// <param name="CodeEditorHeight">The height of each code field, or null to size it to its contents.</param>
public readonly record struct NodeEditorSize(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Width = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? CodeEditorHeight = null)
{
    internal NodeEditorSize Constrain() => new(Constrain(Width, 160), Constrain(CodeEditorHeight, 32));

    private static double? Constrain(double? value, double minimum)
        => value is double dimension && double.IsFinite(dimension) ? Math.Max(minimum, dimension) : null;
}
