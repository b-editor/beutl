using System.ComponentModel.DataAnnotations;

namespace Beutl;

// Shows this property and the one named Second as one value: one row of the property panel, where a vector editor shows
// the two side by side, one layer of the inline view and one row of the graph editor. Each keeps its own value, keyframes
// and expression. Name and ResourceType give the row's name, as they do a property's in DisplayAttribute.
[AttributeUsage(AttributeTargets.Property)]
public sealed class CombinedEditorAttribute(string second) : Attribute
{
    public string Second { get; } = second;

    public string? Name { get; set; }

    public Type? ResourceType { get; set; }

    public string? GetName() => new DisplayAttribute { Name = Name, ResourceType = ResourceType }.GetName();
}
