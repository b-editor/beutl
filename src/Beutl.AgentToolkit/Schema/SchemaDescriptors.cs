namespace Beutl.AgentToolkit.Schema;

public sealed record CapabilitySchema(string SchemaVersion, IReadOnlyList<TypeDescriptor> Types);

public sealed record TypeDescriptor(
    string Type,
    string Discriminator,
    string Category,
    IReadOnlyList<FieldDescriptor> BaseFields,
    IReadOnlyList<PropertyDescriptor> Properties,
    string? DisplayName = null,
    string? Description = null);

public sealed record FieldDescriptor(string Name, string ValueType, object? Default = null);

public sealed record PropertyDescriptor(
    string Name,
    string ValueType,
    object? Default,
    bool Animatable,
    bool SupportsExpression,
    DisplayDescriptor? Display = null,
    RangeDescriptor? Range = null,
    double? Step = null,
    string? Converter = null);

public sealed record DisplayDescriptor(string? Name, string? Description, string? GroupName);

public sealed record RangeDescriptor(double Minimum, double Maximum);
