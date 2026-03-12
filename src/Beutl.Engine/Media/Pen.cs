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

    [Display(Name = nameof(GraphicsStrings.Pen_Brush), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.Pen_DashArray), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Beutl.Collections.CoreList<float>?> DashArray { get; } = Property.Create<CoreList<float>?>();

    [Display(Name = nameof(GraphicsStrings.Pen_DashOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> DashOffset { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.Pen_Thickness), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Thickness { get; } = Property.CreateAnimatable<float>(1);

    [Display(Name = nameof(GraphicsStrings.Pen_MiterLimit), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> MiterLimit { get; } = Property.CreateAnimatable<float>(10);

    [Display(Name = nameof(GraphicsStrings.Pen_StrokeCap), ResourceType = typeof(GraphicsStrings))]
    public IProperty<StrokeCap> StrokeCap { get; } = Property.Create(Media.StrokeCap.Flat);

    [Display(Name = nameof(GraphicsStrings.Pen_StrokeJoin), ResourceType = typeof(GraphicsStrings))]
    public IProperty<StrokeJoin> StrokeJoin { get; } = Property.Create(Media.StrokeJoin.Miter);

    [Display(Name = nameof(GraphicsStrings.Pen_StrokeAlignment), ResourceType = typeof(GraphicsStrings))]
    public IProperty<StrokeAlignment> StrokeAlignment { get; } = Property.Create(Media.StrokeAlignment.Center);

    [Display(Name = nameof(GraphicsStrings.Pen_TrimStart), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> TrimStart { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.Pen_TrimEnd), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> TrimEnd { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.Pen_TrimOffset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> TrimOffset { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(Strings.Pen_Offset), ResourceType = typeof(Strings))]
    public IProperty<float> Offset { get; } = Property.CreateAnimatable<float>(0);
}
