using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Serialization;

namespace Beutl.Media;

public sealed partial class FallbackPathSegment : PathSegment, IFallback;

[FallbackType(typeof(FallbackPathSegment))]
public abstract partial class PathSegment : EngineObject
{
    public abstract IProperty<Point> GetEndPoint();

    public partial class Resource
    {
        /// <summary>
        /// Appends this segment to <paramref name="context"/>.
        /// </summary>
        /// <remarks>
        /// An override reads every parameter it needs from this resource, so it must not reach for
        /// <see cref="EngineObject.Resource.GetOriginal"/>.
        /// </remarks>
        public abstract void ApplyTo(IGeometryContext context);

        public virtual Point? GetEndPoint()
        {
            return null;
        }
    }
}
