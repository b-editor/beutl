using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Brush_PerlinNoise), ResourceType = typeof(Strings))]
public sealed partial class PerlinNoiseBrush : Brush
{
    public PerlinNoiseBrush()
    {
        ScanProperties<PerlinNoiseBrush>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(Strings.BaseFrequencyX), ResourceType = typeof(Strings))]
    public IProperty<float> BaseFrequencyX { get; } = Property.CreateAnimatable(0f);

    [Range(0, 100)]
    [Display(Name = nameof(Strings.BaseFrequencyY), ResourceType = typeof(Strings))]
    public IProperty<float> BaseFrequencyY { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(Strings.Octaves), ResourceType = typeof(Strings))]
    public IProperty<int> Octaves { get; } = Property.CreateAnimatable(1);

    [Display(Name = nameof(Strings.Seed), ResourceType = typeof(Strings))]
    public IProperty<float> Seed { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(Strings.PerlinNoiseType), ResourceType = typeof(Strings))]
    public IProperty<PerlinNoiseType> PerlinNoiseType { get; } = Property.CreateAnimatable(Media.PerlinNoiseType.Turbulence);
}
