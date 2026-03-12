using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.PerlinNoiseBrush), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PerlinNoiseBrush : Brush
{
    public PerlinNoiseBrush()
    {
        ScanProperties<PerlinNoiseBrush>();
    }

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.PerlinNoiseBrush_BaseFrequencyX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> BaseFrequencyX { get; } = Property.CreateAnimatable(0f);

    [Range(0, 100)]
    [Display(Name = nameof(GraphicsStrings.PerlinNoiseBrush_BaseFrequencyY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> BaseFrequencyY { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PerlinNoiseBrush_Octaves), ResourceType = typeof(GraphicsStrings))]
    public IProperty<int> Octaves { get; } = Property.CreateAnimatable(1);

    [Display(Name = nameof(GraphicsStrings.PerlinNoiseBrush_Seed), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Seed { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.PerlinNoiseBrush_PerlinNoiseType), ResourceType = typeof(GraphicsStrings))]
    public IProperty<PerlinNoiseType> PerlinNoiseType { get; } = Property.CreateAnimatable(Media.PerlinNoiseType.Turbulence);
}
