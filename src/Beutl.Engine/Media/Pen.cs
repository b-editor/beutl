using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Media;

public sealed partial class Pen : EngineObject
{
    public Pen()
    {
        ScanProperties<Pen>();
    }

    [Display(Name = nameof(GraphicsStrings.Brush), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Brush?> Brush { get; } = Property.CreateExpression<Brush?>();

    [Display(Name = nameof(GraphicsStrings.Pen_DashArray), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Beutl.Collections.CoreList<float>?> DashArray { get; } = Property.CreateExpression<CoreList<float>?>();

    [Display(Name = nameof(GraphicsStrings.Pen_DashOffset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> DashOffset { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.Pen_Thickness), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Thickness { get; } = Property.CreateAnimatable<float>(1);

    [Display(Name = nameof(GraphicsStrings.Pen_MiterLimit), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> MiterLimit { get; } = Property.CreateAnimatable<float>(10);

    [Display(Name = nameof(GraphicsStrings.Pen_StrokeCap), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<StrokeCap> StrokeCap { get; } = Property.CreateExpression(Media.StrokeCap.Flat);

    [Display(Name = nameof(GraphicsStrings.Pen_StrokeJoin), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<StrokeJoin> StrokeJoin { get; } = Property.CreateExpression(Media.StrokeJoin.Miter);

    [Display(Name = nameof(GraphicsStrings.Pen_StrokeAlignment), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<StrokeAlignment> StrokeAlignment { get; } = Property.CreateExpression(Media.StrokeAlignment.Center);

    [Display(Name = nameof(GraphicsStrings.Pen_TrimStart), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> TrimStart { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.Pen_TrimEnd), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> TrimEnd { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Pen_TrimOffset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> TrimOffset { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.Pen_Offset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Offset { get; } = Property.CreateAnimatable<float>(0);
}
