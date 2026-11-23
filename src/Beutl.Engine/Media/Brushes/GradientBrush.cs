using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Media;

/// <summary>
/// Base class for brushes that draw with a gradient.
/// </summary>
public abstract partial class GradientBrush : Brush
{
    public GradientBrush()
    {
        ScanProperties<GradientBrush>();
    }

    [Display(Name = nameof(GraphicsStrings.GradientBrush_SpreadMethod), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<GradientSpreadMethod> SpreadMethod { get; } = Property.CreateExpression(GradientSpreadMethod.Pad);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_InterpolationSpace), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<GradientInterpolationSpace> InterpolationSpace { get; }
        = Property.CreateExpression(GradientInterpolationSpace.LinearSrgb);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_InterpolatePremultiplied), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> InterpolatePremultiplied { get; } = Property.CreateExpression(false);

    [Display(Name = nameof(GraphicsStrings.GradientBrush_GradientStops), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<GradientStop> GradientStops { get; } = Property.CreateList<GradientStop>();

    public partial class Resource
    {
        public override bool TrySampleOpacity(Rect bounds, Point point, out float opacity)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Translate(0.5f - point.X, 0.5f - point.Y);
            using (var paint = new SKPaint())
            {
                new BrushConstructor(bounds, this, BlendMode.SrcOver, RenderIntent.Preview, null).ConfigurePaint(paint);
                surface.Canvas.DrawPaint(paint);
            }

            using SKPixmap pixels = surface.PeekPixels();
            opacity = pixels.GetPixelColor(0, 0).Alpha / 255f;
            return true;
        }
    }
}
