using Beutl.Media;

namespace Beutl.Converters;

internal sealed class PixelPointJsonConverter : StringJsonConverter<PixelPoint>
{
    protected override string TypeName => "PixelPoint";
    protected override PixelPoint Parse(string s) => PixelPoint.Parse(s);
}
