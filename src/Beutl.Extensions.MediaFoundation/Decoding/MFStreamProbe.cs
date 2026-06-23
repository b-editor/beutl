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

    private static int FindStreamIndex(IMFSourceReader sourceReader, Guid majorType)
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

                if (currentMediaType.MajorType == majorType)
                {
                    return streamIndex;
                }
            }
        }

        return -1;
    }
}
