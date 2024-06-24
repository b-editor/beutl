using MonoMac.CoreMedia;

namespace Beutl.Extensions.AVFoundation.Decoding;

internal static class CMTimeUtilities
{
    public static int ConvertFrameFromTimeStamp(CMTime timestamp, double rate)
    {
        return (int)Math.Round(timestamp.Seconds * rate, MidpointRounding.AwayFromZero);
    }

    public static CMTime ConvertTimeStampFromFrame(int frame, double rate)
    {
        return CMTime.FromSeconds(frame / rate, 1);
    }
}
