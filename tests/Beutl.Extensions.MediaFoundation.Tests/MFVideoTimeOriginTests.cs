using System.Buffers.Binary;
using System.Text;

using Beutl.Embedding.MediaFoundation.Decoding;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFVideoTimeOriginTests
{
    [TestCase(0)]
    [TestCase(1)]
    public void VideoEdit_RemovesCompositionOffsetWithoutShiftingAlignedAudio(int version)
    {
        byte[] track = Track(new[] { 2048L }, 10240, version);
        Assert.That(Read(Mp4(track), 2_000_000), Is.Zero);
    }

    [Test]
    public void NoVideoEdit_PreservesTheFirstVideoTimestamp()
        => Assert.That(Read(Mp4(Track(null)), 2_000_000), Is.EqualTo(2_000_000));

    [Test]
    public void PartialTrim_AddsOnlyTheRemainingGap()
        => Assert.That(Read(Mp4(Track(new[] { 100L })), 2_000_000), Is.EqualTo(1_000_000));

    [Test]
    public void EmptyEditBeforeMedia_UsesTheMediaTrim()
        => Assert.That(Read(Mp4(Track(new[] { -1L, 200L })), 2_000_000), Is.Zero);

    [Test]
    public void VersionOne_HandlesMediaTimesBeyond32Bits()
    {
        byte[] file = Mp4(Track(new[] { 8_000_000_000L }, version: 1));
        Assert.That(Read(file, 80_000_002_000_000), Is.EqualTo(2_000_000));
    }

    [Test]
    public void AudioAndDisabledVideoTracks_DoNotOverrideTheSelectedVideo()
    {
        byte[] file = Mp4(Track(new[] { 0L }, handler: "soun"),
            Track(new[] { 0L }, enabled: false), Track(new[] { 200L }));
        Assert.That(Read(file, 2_000_000), Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void MultipleEnabledVideoTracks_KeepTheOriginalAudioOrigin(int firstEdit)
    {
        // MF's selected video need not be the first enabled MP4 track. Without
        // its track identity, mixing another track's trim with its PTS is unsafe.
        byte[] file = Mp4(Track(new[] { firstEdit * 100L }), Track(new[] { 200L }));
        Assert.That(Read(file, 2_000_000), Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void EmptyEditAfterMedia_RejectsTheNonlinearTimeline(int version)
        => Assert.That(Read(Mp4(Track(new[] { -1L, 100L, -1L }, version: version)), 2_000_000), Is.Zero);

    [Test]
    public void LargeMediaPayload_IsSkippedAndExtendedSizeMetadataIsRead()
    {
        byte[] file = Join(Box("ftyp", "isom"u8.ToArray()), Box("mdat", new byte[1024 * 1024]),
            ExtendedBox("moov", Track(new[] { 200L })));
        Assert.That(Read(file, 2_000_000), Is.Zero);
    }

    [Test]
    public void BoxSizeZero_ExtendsToTheParentEnd()
    {
        byte[] file = Mp4(Track(new[] { 200L }));
        // The ftyp box has 12 bytes; the following moov can extend to EOF.
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12), 0);
        Assert.That(Read(file, 2_000_000), Is.Zero);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NonpositiveFirstTimestamp_DoesNotShiftAudio(long timestamp)
        => Assert.That(Read(Mp4(Track(null)), timestamp), Is.Zero);

    [Test]
    public void OtherContainer_PreservesItsNativeTimeOrigin()
        => Assert.That(Read("RIFF\0\0\0\0WAVE"u8.ToArray(), 2_000_000), Is.EqualTo(2_000_000));

    [TestCaseSource(nameof(UnsupportedMetadata))]
    public void InvalidOrNonlinearEdits_DoNotApplyAnUnconditionalVideoGap(byte[] file)
        => Assert.That(Read(file, 2_000_000), Is.Zero);

    private static IEnumerable<TestCaseData> UnsupportedMetadata()
    {
        yield return new TestCaseData(Mp4(Track(new[] { 0L, 200L }))).SetName("MultipleMediaEdits");
        yield return new TestCaseData(Mp4(Track(new[] { -2L }))).SetName("InvalidNegativeMediaTime");
        yield return new TestCaseData(Mp4(Track(new[] { -1L }))).SetName("OnlyEmptyEdits");
        yield return new TestCaseData(Mp4(Track(new[] { 200L }, rate: 0))).SetName("DwellEdit");
        yield return new TestCaseData(Mp4(Track(new[] { 200L }, rate: 0x20000))).SetName("RateEdit");
        yield return new TestCaseData(Mp4(Track(new[] { 200L }, scale: 0))).SetName("ZeroTimescale");
        yield return new TestCaseData(Mp4(Track(new[] { 200L }, version: 2))).SetName("UnknownVersion");
        yield return new TestCaseData(Mp4(Track(new[] { 200L }, count: uint.MaxValue))).SetName("OversizedEditCount");
        yield return new TestCaseData(Mp4(Track(new[] { long.MaxValue }, scale: 1, version: 1))).SetName("TimeOverflow");
        yield return new TestCaseData(Box("ftyp", "isom"u8.ToArray())).SetName("MissingMovie");

        byte[] truncated = Mp4(Track(new[] { 200L }));
        yield return new TestCaseData(truncated[..^1]).SetName("TruncatedBox");
        byte[] oversized = Mp4(Track(null));
        BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(12), uint.MaxValue);
        yield return new TestCaseData(oversized).SetName("BoxOutsideParent");
        byte[] undersized = Mp4(Track(null));
        BinaryPrimitives.WriteUInt32BigEndian(undersized.AsSpan(12), 4);
        yield return new TestCaseData(undersized).SetName("BoxSmallerThanHeader");
    }

    private static long Read(byte[] data, long timestamp)
    {
        using var stream = new MemoryStream(data);
        return MFVideoTimeOrigin.GetAudioStartTime(stream, timestamp);
    }

    private static byte[] Mp4(params byte[][] tracks)
        => Join(Box("ftyp", "isom"u8.ToArray()), Box("moov", tracks));

    private static byte[] Track(long[]? edits, uint scale = 1000, int version = 0,
        string handler = "vide", bool enabled = true, uint rate = 0x10000, uint? count = null)
    {
        byte[] mdhd = Box("mdhd", UInt32((uint)version << 24), new byte[version == 1 ? 16 : 8], UInt32(scale));
        byte[] mdia = Box("mdia", mdhd, Box("hdlr", new byte[8], Encoding.ASCII.GetBytes(handler)));
        byte[] tkhd = Box("tkhd", UInt32(enabled ? 1u : 0u));
        if (edits == null)
            return Box("trak", tkhd, mdia);

        var entries = new List<byte[]> { UInt32((uint)version << 24), UInt32(count ?? (uint)edits.Length) };
        foreach (long time in edits)
        {
            entries.Add(version == 1 ? UInt64(4000) : UInt32(4000));
            entries.Add(version == 1 ? UInt64(unchecked((ulong)time)) : UInt32(unchecked((uint)time)));
            entries.Add(UInt32(rate));
        }
        return Box("trak", tkhd, mdia, Box("edts", Box("elst", entries.ToArray())));
    }

    private static byte[] Box(string type, params byte[][] parts)
    {
        byte[] body = Join(parts);
        return Join(UInt32((uint)(body.Length + 8)), Encoding.ASCII.GetBytes(type), body);
    }

    private static byte[] ExtendedBox(string type, params byte[][] parts)
    {
        byte[] body = Join(parts);
        return Join(UInt32(1), Encoding.ASCII.GetBytes(type), UInt64((ulong)body.Length + 16), body);
    }

    private static byte[] UInt32(uint value)
    {
        byte[] result = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, value);
        return result;
    }

    private static byte[] UInt64(ulong value)
    {
        byte[] result = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(result, value);
        return result;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
}
