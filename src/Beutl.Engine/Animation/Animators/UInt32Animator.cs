namespace Beutl.Animation.Animators;

public sealed class UInt32Animator : Animator<uint>
{
    public override uint Interpolate(float progress, uint oldValue, uint newValue)
    {
        // 32bit 値も Math.Clamp の上下限 (uint.MinValue/uint.MaxValue) も double で正確に
        // 表現できるため、UInt64Animator のような progress=0/1 の早期 return や自前飽和は不要。
        double delta = (double)newValue - (double)oldValue;
        double v = oldValue + delta * progress;
        return (uint)Math.Round(Math.Clamp(v, uint.MinValue, uint.MaxValue), MidpointRounding.AwayFromZero);
    }
}
