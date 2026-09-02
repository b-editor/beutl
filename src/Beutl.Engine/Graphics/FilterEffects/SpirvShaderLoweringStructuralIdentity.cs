namespace Beutl.Graphics.Effects;

internal sealed class SpirvShaderLoweringStructuralIdentity(
    string source,
    SpirvPushConstantBinding[] pushConstants,
    bool supportsBitExactSkiaHandoff)
    : IEquatable<SpirvShaderLoweringStructuralIdentity>
{
    public bool Equals(SpirvShaderLoweringStructuralIdentity? other)
        => other is not null
           && source == other.Source
           && supportsBitExactSkiaHandoff == other.SupportsBitExactSkiaHandoff
           && pushConstants.AsSpan().SequenceEqual(other.PushConstants);

    public override bool Equals(object? obj)
        => obj is SpirvShaderLoweringStructuralIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(source, StringComparer.Ordinal);
        hash.Add(supportsBitExactSkiaHandoff);
        foreach (SpirvPushConstantBinding item in pushConstants)
            hash.Add(item);
        return hash.ToHashCode();
    }

    private string Source => source;

    private SpirvPushConstantBinding[] PushConstants => pushConstants;

    private bool SupportsBitExactSkiaHandoff => supportsBitExactSkiaHandoff;
}
