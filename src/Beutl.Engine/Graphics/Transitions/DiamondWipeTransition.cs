using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>Reveals the incoming clip inside a diamond expanding from the centre to all four corners.</summary>
[Display(Name = nameof(GraphicsStrings.DiamondWipeTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DiamondWipeTransition : ClipTransition
{
    public DiamondWipeTransition()
    {
        ScanProperties<DiamondWipeTransition>();
    }

    public new partial class Resource
    {
        private readonly GeometryMask _mask = new();

        public override void Draw(ClipTransitionContext context)
        {
            using var path = new GeometryContext();
            Size size = context.Graphics.Size;
            Point center = new Rect(size).Center;
            float radius = (size.Width + size.Height) * context.Progress / 2;
            path.MoveTo(new Point(center.X, center.Y - radius));
            path.LineTo(new Point(center.X + radius, center.Y));
            path.LineTo(new Point(center.X, center.Y + radius));
            path.LineTo(new Point(center.X - radius, center.Y));
            path.Close();
            _mask.Draw(context, path);
        }

        partial void PostDispose(bool disposing) => _mask.Dispose();
    }
}
