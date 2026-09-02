namespace Beutl.Graphics.Effects;

internal sealed record ShaderResourceStructuralIdentity(
    string Name,
    ShaderResourceCoordinateSpace CoordinateSpace,
    object DefinitionFingerprint);
