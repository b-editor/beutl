namespace Beutl.Media;

public sealed class CloseOperation : PathOperation
{
    public override void ApplyTo(IGeometryContext context)
    {
        context.Close();
    }
}
