using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Media;

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
