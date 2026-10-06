using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Embedding.MediaFoundation.Decoding;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
[Platform("Win")]
[NonParallelizable]
public class MFStreamProbeTests
{
    private const int InvalidStreamNumber = unchecked((int)0xC00D36B3);

    [Test]
    public void EnumerateSelectedStreams_InvalidStreamNumber_Completes()
    {
        WithMediaTypeFailure(InvalidStreamNumber, reader =>
        {
            Assert.That(MFStreamProbe.EnumerateSelectedStreams(reader).ToArray(), Is.Empty);
            Assert.That(MFStreamProbe.FindVideoStreamIndex(reader), Is.EqualTo(-1));
        });
    }

    [TestCase(unchecked((int)0x80004005))] // E_FAIL
    [TestCase(unchecked((int)0x8007000E))] // E_OUTOFMEMORY
    public void EnumerateSelectedStreams_OtherHResult_Propagates(int resultCode)
    {
        WithMediaTypeFailure(resultCode, reader =>
        {
            SharpGenException? exception = Assert.Throws<SharpGenException>(() =>
                _ = MFStreamProbe.EnumerateSelectedStreams(reader).ToArray());

            Assert.That(exception!.ResultCode.Code, Is.EqualTo(resultCode));
        });
    }

    [Test]
    public void EnumerateSelectedStreams_DeselectedVideo_PreservesOtherStreamsAndIndices()
    {
        MediaFactory.MFStartup();
        try
        {
            string file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.mp4");
            using var attributes = MediaFactory.MFCreateAttributes(1u);
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(file, attributes);
            var streams = MFStreamProbe.EnumerateSelectedStreams(reader).ToArray();

            Assert.That(streams, Has.Length.EqualTo(2));
            int videoIndex = streams.Single(stream => stream.MajorType == MediaTypeGuids.Video).StreamIndex;
            reader.SetStreamSelection(videoIndex, false);

            Assert.Multiple(() =>
            {
                Assert.That(MFStreamProbe.EnumerateSelectedStreams(reader).ToArray(),
                    Is.EqualTo(streams.Where(stream => stream.StreamIndex != videoIndex).ToArray()));
                Assert.That(MFStreamProbe.FindVideoStreamIndex(reader), Is.EqualTo(-1));
            });

            reader.SetStreamSelection(videoIndex, true);
            Assert.That(MFStreamProbe.FindVideoStreamIndex(reader), Is.EqualTo(videoIndex));
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
    }

    private static unsafe void WithMediaTypeFailure(int resultCode, Action<IMFSourceReader> assertions)
    {
        // This failure path only calls IMFSourceReader::GetCurrentMediaType (slot 6)
        // and IUnknown::Release (slot 2). Keep both allocations alive until disposal.
        nint* vtable = stackalloc nint[7];
        vtable[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release;
        vtable[6] = (nint)(delegate* unmanaged[Stdcall]<nint, int, nint*, int>)&GetCurrentMediaType;
        nint* instance = stackalloc nint[2] { (nint)vtable, resultCode };
        using var reader = new IMFSourceReader((nint)instance);
        assertions(reader);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(nint instance) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int GetCurrentMediaType(nint instance, int streamIndex, nint* mediaType)
    {
        *mediaType = 0;
        return (int)((nint*)instance)[1];
    }
}
