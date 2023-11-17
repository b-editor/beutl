namespace Beutl.Media;

public interface IPerlinNoiseBrush : IBrush
{
    float BaseFrequencyX { get; }

    float BaseFrequencyY { get; }

    int Octaves { get; }

    float Seed { get; }

    PerlinNoiseType PerlinNoiseType { get; }
}
