using System.ComponentModel.DataAnnotations;

namespace Beutl;

// Shows this property and the one named Second, or the ones named Second and Third, as one value: one row of the property
// panel, where a vector editor shows them side by side, one layer of the inline view and one row of the graph editor. Each
// keeps its own value, keyframes and expression. Two float or int properties join as a point or a size, and three float
// properties as a Vector3. Name and ResourceType give the row's name, as they do a property's in DisplayAttribute.
[AttributeUsage(AttributeTargets.Property)]
public sealed class CombinedEditorAttribute(string second) : Attribute
{
    public CombinedEditorAttribute(string second, string third)
        : this(second)
    {
        Third = third;
    }

    public string Second { get; } = second;

    public string? Third { get; }

    public string? Name { get; set; }

    public Type? ResourceType { get; set; }

    public string? GetName() => new DisplayAttribute { Name = Name, ResourceType = ResourceType }.GetName();
}
