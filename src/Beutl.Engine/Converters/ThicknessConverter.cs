using Beutl.Graphics;

namespace Beutl.Converters;

public sealed class ThicknessConverter : FourFloatComponentTypeConverter<Thickness>
{
    protected override (float, float, float, float) GetFourComponents(Thickness v) => (v.Left, v.Top, v.Right, v.Bottom);
    protected override (float, float) GetTwoComponents(Thickness v) => (v.Left, v.Top);
    protected override Thickness FromUniform(float f) => new(f);
    protected override Thickness FromTwo(float a, float b) => new(a, b);
    protected override Thickness FromFour(float a, float b, float c, float d) => new(a, b, c, d);
    protected override Thickness Parse(string s) => Thickness.Parse(s);
}
