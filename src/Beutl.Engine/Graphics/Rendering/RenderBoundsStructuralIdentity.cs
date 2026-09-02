namespace Beutl.Graphics.Rendering;

internal readonly record struct RenderBoundsStructuralIdentity(
    RenderBoundsContractKind Kind,
    object? ForwardMap,
    object? BackwardMap)
{
    public static RenderBoundsStructuralIdentity Identity { get; } =
        new(RenderBoundsContractKind.Identity, null, null);

    public static RenderBoundsStructuralIdentity FullInput { get; } =
        new(RenderBoundsContractKind.FullInput, null, null);

    public static RenderBoundsStructuralIdentity Create(
        Delegate transformBounds,
        Delegate getRequiredInputBounds)
        => new(
            RenderBoundsContractKind.Custom,
            RenderDescriptionValidation.StructuralIdentityOf(transformBounds),
            RenderDescriptionValidation.StructuralIdentityOf(getRequiredInputBounds));

    public static RenderBoundsStructuralIdentity CreateFullInput(Delegate transformBounds)
        => new(
            RenderBoundsContractKind.CustomFullInput,
            RenderDescriptionValidation.StructuralIdentityOf(transformBounds),
            null);
}
