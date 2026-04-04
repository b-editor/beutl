using Beutl.Media;

namespace Beutl.FFmpegWorker.Handlers;

internal static class ColorSpaceIpcHelper
{
    public static (float[] TransferFn, float[] ToXyzD50) Extract(BitmapColorSpace colorSpace)
    {
        var transferFn = colorSpace.GetNumericalTransferFunction();
        var xyz = colorSpace.ToColorSpaceXyz();

        float[] tfn = [transferFn.G, transferFn.A, transferFn.B, transferFn.C, transferFn.D, transferFn.E, transferFn.F];
        float[] toXyzD50 = xyz.Values.ToArray();

        return (tfn, toXyzD50);
    }

    public static bool CheckAndUpdate(
        float[] currentTransferFn, float[] currentToXyzD50,
        ref float[]? lastTransferFn, ref float[]? lastToXyzD50)
    {
        bool changed = !currentTransferFn.AsSpan().SequenceEqual(lastTransferFn)
                       || !currentToXyzD50.AsSpan().SequenceEqual(lastToXyzD50);
        if (changed)
        {
            lastTransferFn = currentTransferFn;
            lastToXyzD50 = currentToXyzD50;
        }

        return changed;
    }
}
