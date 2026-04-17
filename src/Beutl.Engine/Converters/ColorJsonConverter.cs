using Beutl.Media;

namespace Beutl.Converters;

internal sealed class ColorJsonConverter : StringJsonConverter<Color>
{
    protected override string TypeName => "Color";
    protected override Color Parse(string s) => Color.Parse(s);
}
