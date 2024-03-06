using Beutl.Graphics;

namespace Beutl.Media;

public sealed class CloseOperation : PathOperation
{
    public override void ApplyTo(IGeometryContext context)
    {
        context.Close();
    }

    public override bool TryGetEndPoint(out Point point)
    {
        point = default;
        return false;
    }
}
