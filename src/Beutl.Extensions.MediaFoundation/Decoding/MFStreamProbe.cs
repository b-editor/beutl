using SharpGen.Runtime;
using Vortice.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

internal static class MFStreamProbe
{
    // MF_E_INVALIDSTREAMNUMBER: the source reader has no stream at the requested index, i.e. we
    // have walked past the end of the stream list. Any other HRESULT is a real failure.
    private const int MF_E_INVALIDSTREAMNUMBER = unchecked((int)0xC00D36B3);

    public static bool HasAudioStream(string file)
    {
        using var attributes = MediaFactory.MFCreateAttributes(1u);
        using var sourceReader = MediaFactory.MFCreateSourceReaderFromURL(file, attributes);
        return FindStreamIndex(sourceReader, MediaTypeGuids.Audio) != -1;
    }

    public static int FindVideoStreamIndex(IMFSourceReader sourceReader)
        => FindStreamIndex(sourceReader, MediaTypeGuids.Video);

    public static long GetFirstVideoTimestamp(string file)
    {
        using var attributes = MediaFactory.MFCreateAttributes(1u);
        using var sourceReader = MediaFactory.MFCreateSourceReaderFromURL(file, attributes);
        int stream = FindVideoStreamIndex(sourceReader);
        if (stream == -1)
            return 0;

        sourceReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        sourceReader.SetStreamSelection(stream, true);
        return ReadFirstVideoTimestamp(() =>
        {
            // Read the compressed sample: an Audio-only open does not need to
            // initialize a video decoder just to use the same time origin.
            using var sample = sourceReader.ReadSample(stream, SourceReaderControlFlag.None,
                out _, out var flags, out long timestamp);
            return (sample != null, flags, timestamp);
        });
    }

    internal static long ReadFirstVideoTimestamp(Func<(bool HasSample, SourceReaderFlag Flags, long Timestamp)> readSample)
    {
        while (true)
        {
            var (hasSample, flags, timestamp) = readSample();
            // MF can return success with an error flag. Its reader must not be
            // called again after that, even when this result includes a sample.
            if (flags.HasFlag(SourceReaderFlag.Error))
                return 0;
            if (hasSample)
                return timestamp;
            if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                return 0;
        }
    }

    private static int FindStreamIndex(IMFSourceReader sourceReader, Guid majorType)
    {
        foreach ((int streamIndex, Guid streamMajorType) in EnumerateSelectedStreams(sourceReader))
        {
            if (streamMajorType == majorType)
            {
                return streamIndex;
            }
        }

        return -1;
    }

    public static IEnumerable<(int StreamIndex, Guid MajorType)> EnumerateSelectedStreams(IMFSourceReader sourceReader)
    {
        for (int streamIndex = 0; true; ++streamIndex)
        {
            IMFMediaType currentMediaType;
            try
            {
                currentMediaType = sourceReader.GetCurrentMediaType(streamIndex);
            }
            catch (SharpGenException ex) when (ex.ResultCode.Code == MF_E_INVALIDSTREAMNUMBER)
            {
                break;
            }

            using (currentMediaType)
            {
                if (!sourceReader.GetStreamSelection(streamIndex))
                {
                    continue;
                }

                yield return (streamIndex, currentMediaType.MajorType);
            }
        }
    }
}
