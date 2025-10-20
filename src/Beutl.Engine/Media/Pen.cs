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

    [Display(Name = nameof(Strings.Brush), ResourceType = typeof(Strings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(Strings.Pen_DashArray), ResourceType = typeof(Strings))]
    public IProperty<Beutl.Collections.CoreList<float>?> DashArray { get; } = Property.Create<CoreList<float>?>();

    [Display(Name = nameof(Strings.Pen_DashOffset), ResourceType = typeof(Strings))]
    public IProperty<float> DashOffset { get; } = Property.Create<float>(0);

    [Display(Name = nameof(Strings.Thickness), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Thickness { get; } = Property.Create<float>(1);

    [Display(Name = nameof(Strings.Pen_MiterLimit), ResourceType = typeof(Strings))]
    public IProperty<float> MiterLimit { get; } = Property.Create<float>(10);

    [Display(Name = nameof(Strings.Pen_StrokeCap), ResourceType = typeof(Strings))]
    public IProperty<StrokeCap> StrokeCap { get; } = Property.Create(Media.StrokeCap.Flat);

    [Display(Name = nameof(Strings.Pen_StrokeJoin), ResourceType = typeof(Strings))]
    public IProperty<StrokeJoin> StrokeJoin { get; } = Property.Create(Media.StrokeJoin.Miter);

    [Display(Name = nameof(Strings.Pen_StrokeAlignment), ResourceType = typeof(Strings))]
    public IProperty<StrokeAlignment> StrokeAlignment { get; } = Property.Create(Media.StrokeAlignment.Center);
}
