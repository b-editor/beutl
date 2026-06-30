using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Schema;

public sealed record CapabilitySchema(
    string SchemaVersion,
    IReadOnlyList<TypeDescriptor> Types,
    IReadOnlyList<DeclarativeExample> Examples);

public sealed record DeclarativeExample(string Name, string Description, JsonObject Patch);

public sealed record DeclarativeExampleSummary(
    string Name,
    string Description,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags);

public sealed record EffectSummary(
    string Name,
    string Type,
    string Discriminator,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> IntentTags,
    IReadOnlyList<string> PropertyNames,
    IReadOnlyList<string> Notes);

public sealed record EffectRecipeSummary(
    string Name,
    string Description,
    IReadOnlyList<string> IntentTags,
    IReadOnlyList<string> EffectNames,
    IReadOnlyList<string> Notes);

public sealed record EffectRecipe(
    string Name,
    string Description,
    IReadOnlyList<string> IntentTags,
    IReadOnlyList<string> EffectNames,
    IReadOnlyList<string> Notes,
    JsonObject Patch);

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
    string? Converter = null,
    string? ElementType = null,
    IReadOnlyList<string>? EnumValues = null,
    string? UsageHint = null);

public sealed record DisplayDescriptor(string? Name, string? Description, string? GroupName);

public sealed record RangeDescriptor(double Minimum, double Maximum);
