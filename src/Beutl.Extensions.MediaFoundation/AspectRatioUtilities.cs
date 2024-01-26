// https://github.com/amate/MFVideoReader

using SharpDX.MediaFoundation;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

#pragma warning disable CA1416 // プラットフォームの互換性を検証

internal class AspectRatioUtilities
{
    public static RECT CorrectAspectRatio(in RECT src, in Ratio srcPAR, in Ratio destPAR)
    {
        // Start with a rectangle the same size as src, but offset to (0,0).
        RECT rc = RECT.FromXYWH(0, 0, src.right - src.left, src.bottom - src.top);

        // If the source and destination have the same PAR, there is nothing to do.
        // Otherwise, adjust the image size, in two steps:
        //  1. Transform from source PAR to 1:1
        //  2. Transform from 1:1 to destination PAR.

        if ((srcPAR.Numerator != destPAR.Numerator) ||
            (srcPAR.Denominator != destPAR.Denominator))
        {
            // Correct for the source's PAR.

            if (srcPAR.Numerator > srcPAR.Denominator)
            {
                // The source has "wide" pixels, so stretch the width.
                rc.right = PInvoke.MulDiv(rc.right, srcPAR.Numerator, srcPAR.Denominator);
            }
            else if (srcPAR.Numerator < srcPAR.Denominator)
            {
                // The source has "tall" pixels, so stretch the height.
                rc.bottom = PInvoke.MulDiv(rc.bottom, srcPAR.Denominator, srcPAR.Numerator);
            }
            // else: PAR is 1:1, which is a no-op.

            // Next, correct for the target's PAR. This is the inverse operation of 
            // the previous.

            if (destPAR.Numerator > destPAR.Denominator)
            {
                // The destination has "wide" pixels, so stretch the height.
                rc.bottom = PInvoke.MulDiv(rc.bottom, destPAR.Numerator, destPAR.Denominator);
            }
            else if (destPAR.Numerator < destPAR.Denominator)
            {
                // The destination has "tall" pixels, so stretch the width.
                rc.right = PInvoke.MulDiv(rc.right, destPAR.Denominator, destPAR.Numerator);
            }
            // else: PAR is 1:1, which is a no-op.
        }

        return rc;
    }

    public static RECT CorrectAspectRatio(in RECT src, in MFRatio srcPAR, in MFRatio destPAR)
    {
        // Start with a rectangle the same size as src, but offset to (0,0).
        RECT rc = RECT.FromXYWH(0, 0, src.right - src.left, src.bottom - src.top);

        // If the source and destination have the same PAR, there is nothing to do.
        // Otherwise, adjust the image size, in two steps:
        //  1. Transform from source PAR to 1:1
        //  2. Transform from 1:1 to destination PAR.

        if ((srcPAR.Numerator != destPAR.Numerator) ||
            (srcPAR.Denominator != destPAR.Denominator))
        {
            // Correct for the source's PAR.

            if (srcPAR.Numerator > srcPAR.Denominator)
            {
                // The source has "wide" pixels, so stretch the width.
                rc.right = PInvoke.MulDiv(rc.right, (int)srcPAR.Numerator, (int)srcPAR.Denominator);
            }
            else if (srcPAR.Numerator < srcPAR.Denominator)
            {
                // The source has "tall" pixels, so stretch the height.
                rc.bottom = PInvoke.MulDiv(rc.bottom, (int)srcPAR.Denominator, (int)srcPAR.Numerator);
            }
            // else: PAR is 1:1, which is a no-op.

            // Next, correct for the target's PAR. This is the inverse operation of 
            // the previous.

            if (destPAR.Numerator > destPAR.Denominator)
            {
                // The destination has "wide" pixels, so stretch the height.
                rc.bottom = PInvoke.MulDiv(rc.bottom, (int)destPAR.Numerator, (int)destPAR.Denominator);
            }
            else if (destPAR.Numerator < destPAR.Denominator)
            {
                // The destination has "tall" pixels, so stretch the width.
                rc.right = PInvoke.MulDiv(rc.right, (int)destPAR.Denominator, (int)destPAR.Numerator);
            }
            // else: PAR is 1:1, which is a no-op.
        }

        return rc;
    }
}
