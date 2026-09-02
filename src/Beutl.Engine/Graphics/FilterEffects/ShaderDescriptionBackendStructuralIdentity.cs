namespace Beutl.Graphics.Effects;

internal sealed record ShaderDescriptionBackendStructuralIdentity(
    ShaderProgramBackend Backend,
    object DescriptionIdentity);
