using Beutl.Media;

namespace Beutl.FFmpegIpc.Protocol;

/// <summary>
/// A color space as the IPC messages carry it: the seven parameters of its transfer function, G and A to F, and the nine
/// entries of its matrix to XYZ D50, row by row.
/// </summary>
public static class IpcColorSpace
{
    public static (float[] TransferFn, float[] ToXyzD50) ToArrays(BitmapColorSpace colorSpace)
    {
        BitmapColorSpaceTransferFn fn = colorSpace.GetNumericalTransferFunction();
        return ([fn.G, fn.A, fn.B, fn.C, fn.D, fn.E, fn.F], colorSpace.ToColorSpaceXyz().Values.ToArray());
    }

    /// <exception cref="InvalidOperationException">An array has the wrong length.</exception>
    public static BitmapColorSpace FromArrays(float[] transferFn, float[] toXyzD50)
    {
        if (transferFn.Length != 7 || toXyzD50.Length != 9)
        {
            throw new InvalidOperationException(
                $"A color space takes 7 transfer function parameters and 9 matrix entries, not {transferFn.Length} and {toXyzD50.Length}.");
        }

        var fn = new BitmapColorSpaceTransferFn
        {
            G = transferFn[0],
            A = transferFn[1],
            B = transferFn[2],
            C = transferFn[3],
            D = transferFn[4],
            E = transferFn[5],
            F = transferFn[6]
        };
        var xyz = BitmapColorSpaceXyz.Create(toXyzD50);

        // sRGB and linear sRGB keep their shared instances.
        if (xyz == BitmapColorSpaceXyz.Srgb && fn == BitmapColorSpaceTransferFn.Srgb)
            return BitmapColorSpace.Srgb;
        if (xyz == BitmapColorSpaceXyz.Srgb && fn == BitmapColorSpaceTransferFn.Linear)
            return BitmapColorSpace.LinearSrgb;

        return BitmapColorSpace.CreateRgb(fn, xyz);
    }
}
