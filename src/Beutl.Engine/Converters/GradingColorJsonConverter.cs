using Beutl.Media;

namespace Beutl.Converters;

internal sealed class GradingColorJsonConverter : StringJsonConverter<GradingColor>
{
    protected override string TypeName => "GradingColor";
    protected override GradingColor Parse(string s) => GradingColor.Parse(s);
}
