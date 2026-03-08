using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Serialization;

namespace Beutl.Media;

public sealed partial class FallbackPathSegment : PathSegment, IFallback;

[FallbackType(typeof(FallbackPathSegment))]
public abstract partial class PathSegment : EngineObject
{
    public abstract void ApplyTo(IGeometryContext context, Resource resource);

    public abstract IProperty<Point> GetEndPoint();

    public partial class Resource
    {
        public virtual Point? GetEndPoint()
        {
            return null;
        }
    }
}
