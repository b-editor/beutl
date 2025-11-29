using System.ComponentModel.DataAnnotations;
using Beutl.Engine;

namespace Beutl.Media;

public sealed partial class PerlinNoiseBrush : Brush
{
    public PerlinNoiseBrush()
    {
        ScanProperties<PerlinNoiseBrush>();
    }

    [Range(0, 100)]
    public IProperty<float> BaseFrequencyX { get; } = Property.CreateAnimatable(0f);

    [Range(0, 100)]
    public IProperty<float> BaseFrequencyY { get; } = Property.CreateAnimatable(0f);

    public IProperty<int> Octaves { get; } = Property.CreateAnimatable(1);

    public IProperty<float> Seed { get; } = Property.CreateAnimatable(0f);

    public IProperty<PerlinNoiseType> PerlinNoiseType { get; } = Property.CreateAnimatable(Media.PerlinNoiseType.Turbulence);
}
