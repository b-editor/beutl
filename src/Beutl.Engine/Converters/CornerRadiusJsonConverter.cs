using Beutl.Media;

namespace Beutl.Converters;

internal sealed class CornerRadiusJsonConverter : StringJsonConverter<CornerRadius>
{
    protected override string TypeName => "CornerRadius";
    protected override CornerRadius Parse(string s) => CornerRadius.Parse(s);
}
