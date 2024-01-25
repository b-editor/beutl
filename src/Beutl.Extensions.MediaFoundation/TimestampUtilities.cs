#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public static class TimestampUtilities
{
    public static double ConvertSecFrom100ns(long hnsTime)
    {
        return hnsTime / 10000000.0;
    }

    public static long Convert100nsFromSec(double sec)
    {
        return (long)(sec * 10000000);
    }

    public static int ConvertFrameFromTimeStamp(long nsTimeStamp, uint nume, uint denom)
    {
        double frame = ConvertSecFrom100ns(nsTimeStamp) * nume / denom;
        return (int)Math.Round(frame, MidpointRounding.AwayFromZero);
    }

    // frame -> timestamp
    public static long ConvertTimeStampFromFrame(long frame, uint nume, uint denom)
    {
        double frameSec = (double)(frame * denom) / nume;
        return Convert100nsFromSec(frameSec);
    }

    // timestamp -> Sample
    public static int ConvertSampleFromTimeStamp(long nsTimeStamp, int nSamplesPerSec)
    {
        double sample = ConvertSecFrom100ns(nsTimeStamp) * nSamplesPerSec;
        return (int)Math.Round(sample, MidpointRounding.AwayFromZero);
    }

    // Sample -> timestamp
    public static long ConvertTimeStampFromSample(int startSample, int nSamplesPerSec)
    {
        double sampleSec = (double)startSample / nSamplesPerSec;
        return Convert100nsFromSec(sampleSec);
    }
}
