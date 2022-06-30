using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure.Drawable.Foreground;

public class Set_LinearGradientBrush : LayerOperation
{
    private readonly LinearGradientBrush _brush = new();

    public float Opacity
    {
        get => _brush.Opacity;
        set => _brush.Opacity = value;
    }

    public ITransform? Transform { get; set; }

    public RelativePoint TransformOrigin { get; set; }

    public GradientSpreadMethod SpreadMethod { get; set; }

    public GradientStops? GradientStops { get; set; }

    public RelativePoint StartPoint { get; set; }

    public RelativePoint EndPoint { get; set; }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is Graphics.Drawable drawable)
        {

        }
        base.RenderCore(ref args);
    }
}
