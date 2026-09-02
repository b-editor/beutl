namespace Beutl.AgentToolkit.Tools;

public sealed record ValidateShaderResponse(
    string SchemaVersion,
    string EffectType,
    string Status,
    string? Error,
    string Hint);
