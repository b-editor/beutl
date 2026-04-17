using Beutl.Media;

namespace Beutl.Converters;

internal sealed class PixelSizeJsonConverter : StringJsonConverter<PixelSize>
{
    protected override string TypeName => "PixelSize";
    protected override PixelSize Parse(string s) => PixelSize.Parse(s);
}
