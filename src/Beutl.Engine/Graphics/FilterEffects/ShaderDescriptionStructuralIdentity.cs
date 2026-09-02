using SkiaSharp;

namespace Beutl.Graphics.Effects;

internal sealed class ShaderDescriptionStructuralIdentity(
    ShaderDescriptionKind kind,
    string source,
    object? spirvLowering,
    object bounds,
    object inputDemand,
    object? hitTest,
    SKShaderTileMode tileMode,
    ShaderBindingStructuralIdentity[] uniforms,
    ShaderResourceStructuralIdentity[] resources)
    : IEquatable<ShaderDescriptionStructuralIdentity>
{
    public bool Equals(ShaderDescriptionStructuralIdentity? other)
        => other is not null
           && kind == other.Kind
           && source == other.Source
           && Equals(spirvLowering, other.SpirvLowering)
           && Equals(bounds, other.Bounds)
           && Equals(inputDemand, other.InputDemand)
           && Equals(hitTest, other.HitTest)
           && tileMode == other.TileMode
           && uniforms.AsSpan().SequenceEqual(other.Uniforms)
           && resources.AsSpan().SequenceEqual(other.Resources);

    public override bool Equals(object? obj) => obj is ShaderDescriptionStructuralIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(kind);
        hash.Add(source, StringComparer.Ordinal);
        hash.Add(spirvLowering);
        hash.Add(bounds);
        hash.Add(inputDemand);
        hash.Add(hitTest);
        hash.Add(tileMode);
        foreach (ShaderBindingStructuralIdentity item in uniforms)
            hash.Add(item);
        foreach (ShaderResourceStructuralIdentity item in resources)
            hash.Add(item);
        return hash.ToHashCode();
    }

    private ShaderDescriptionKind Kind => kind;
    private string Source => source;
    private object? SpirvLowering => spirvLowering;
    private object Bounds => bounds;
    private object InputDemand => inputDemand;
    private object? HitTest => hitTest;
    private SKShaderTileMode TileMode => tileMode;
    private ShaderBindingStructuralIdentity[] Uniforms => uniforms;
    private ShaderResourceStructuralIdentity[] Resources => resources;
}
