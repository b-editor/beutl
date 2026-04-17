using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class ThicknessJsonConverter : StringJsonConverter<Thickness>
{
    protected override string TypeName => "Thickness";
    protected override Thickness Parse(string s) => Thickness.Parse(s);
}
