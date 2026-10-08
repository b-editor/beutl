// https://github.com/amate/MFVideoReader

using Windows.Win32.Media.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

internal static class TimestampUtilities
{
    public static double ConvertSecFrom100ns(long hnsTime)
    {
        return hnsTime / 10000000.0;
    }

    public static long Convert100nsFromSec(double sec)
    {
        return (long)(sec * 10000000);
    }

    public static int ConvertFrameFromTimeStamp(long nsTimeStamp, MFRatio rate)
    {
        double frame = ConvertSecFrom100ns(nsTimeStamp) * rate.Numerator / rate.Denominator;
        return (int)Math.Round(frame, MidpointRounding.AwayFromZero);
    }

    // frame -> timestamp
    public static long ConvertTimeStampFromFrame(long frame, MFRatio rate)
    {
        double frameSec = (double)(frame * rate.Denominator) / rate.Numerator;
        return Convert100nsFromSec(frameSec);
    }
}
