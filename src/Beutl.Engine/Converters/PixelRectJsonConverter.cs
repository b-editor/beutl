using Beutl.Media;

namespace Beutl.Converters;

internal sealed class PixelRectJsonConverter : StringJsonConverter<PixelRect>
{
    protected override string TypeName => "PixelRect";
    protected override PixelRect Parse(string s) => PixelRect.Parse(s);
}
