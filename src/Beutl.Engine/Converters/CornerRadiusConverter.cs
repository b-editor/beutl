using Beutl.Media;

namespace Beutl.Converters;

public sealed class CornerRadiusConverter : FourFloatComponentTypeConverter<CornerRadius>
{
    protected override (float, float, float, float) GetFourComponents(CornerRadius v) => (v.TopLeft, v.TopRight, v.BottomRight, v.BottomLeft);
    protected override (float, float) GetTwoComponents(CornerRadius v) => (v.TopLeft, v.BottomLeft);
    protected override CornerRadius FromUniform(float f) => new(f);
    protected override CornerRadius FromTwo(float a, float b) => new(a, b);
    protected override CornerRadius FromFour(float a, float b, float c, float d) => new(a, b, c, d);
    protected override CornerRadius Parse(string s) => CornerRadius.Parse(s);
}
