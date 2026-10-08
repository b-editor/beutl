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
}
