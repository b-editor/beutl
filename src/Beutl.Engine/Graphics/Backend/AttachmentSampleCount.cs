using System.Numerics;

namespace Beutl.Graphics.Backend;

internal static class AttachmentSampleCount
{
    public const int Maximum = 64;

    public static void Validate(int sampleCount)
    {
        if (sampleCount > Maximum || !BitOperations.IsPow2(sampleCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount), sampleCount, "The sample count must be a power of two from 1 through 64.");
        }
    }
}
