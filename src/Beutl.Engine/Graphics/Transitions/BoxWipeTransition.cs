using System.ComponentModel.DataAnnotations;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Transitions;

/// <summary>Reveals the incoming clip inside a rectangle expanding from the centre of the frame.</summary>
[Display(Name = nameof(GraphicsStrings.BoxWipeTransition), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BoxWipeTransition : ClipTransition
{
    public BoxWipeTransition()
    {
        ScanProperties<BoxWipeTransition>();
    }

    public new partial class Resource
    {
        private readonly GeometryMask _mask = new();

        public override void Draw(ClipTransitionContext context)
        {
            using var path = new GeometryContext();
            Size size = context.Graphics.Size;
            float amount = context.Progress;
            GeometryMask.AddRectangle(path, new Rect(
                size.Width * (1 - amount) / 2, size.Height * (1 - amount) / 2,
                size.Width * amount, size.Height * amount));
            _mask.Draw(context, path);
        }

        partial void PostDispose(bool disposing) => _mask.Dispose();
    }
}
