namespace Beutl.Animation.Animators;

public sealed class Int32Animator : Animator<int>
{
    public override int Interpolate(float progress, int oldValue, int newValue)
    {
        // 32bit 値も Math.Clamp の上下限 (int.MinValue/int.MaxValue) も double で正確に
        // 表現できるため、Int64Animator のような progress=0/1 の早期 return や自前飽和は不要。
        double delta = (double)newValue - oldValue;
        double v = oldValue + delta * progress;
        return (int)Math.Round(Math.Clamp(v, int.MinValue, int.MaxValue), MidpointRounding.AwayFromZero);
    }
}
