using Beutl.Serialization;

namespace Beutl.Media;

public sealed partial class FallbackPathSegment : PathSegment, IFallback
{
    public partial class Resource
    {
        // A segment whose type did not load adds nothing to the path.
        public override void ApplyTo(IGeometryContext context)
        {
        }
    }
}
