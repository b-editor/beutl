using System.Runtime.InteropServices;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegWorker.Encoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture, NonParallelizable]
public sealed class EncoderOptionsQueryMetadataTests
{
    [Test]
    public unsafe void MapsNativeMetadataAndReleasesAllocationsWithoutInstalledLibraries()
    {
        // Save and restore the global bindings so native integration tests remain independent.
        var allocate = vectors.avcodec_alloc_context3;
        var release = vectors.avcodec_free_context;
        var next = vectors.av_opt_next;
        var get = vectors.av_opt_get;
        var free = vectors.av_free;
        var strings = new List<nint>();
        var options = (AVOption*)NativeMemory.AllocZeroed(11, (nuint)sizeof(AVOption));
        var context = (AVCodecContext*)NativeMemory.AllocZeroed((nuint)sizeof(AVCodecContext));
        int releasedContexts = 0;
        int releasedValues = 0;
        var defaults = new Dictionary<string, string>
        {
            ["quality"] = "22",
            ["deadline"] = "1000000",
            ["profile"] = "main10",
            ["ratio"] = "0.5",
            ["enabled"] = "-1",
            ["flags"] = "0",
        };
        byte* Utf8(string? value)
        {
            if (value == null) return null;
            nint pointer = Marshal.StringToCoTaskMemUTF8(value);
            strings.Add(pointer);
            return (byte*)pointer;
        }
        AVOption Option(string? name, AVOptionType type, double min, double max, string? unit = null)
            => new()
            {
                name = Utf8(name),
                help = Utf8("説明"),
                type = type,
                min = min,
                max = max,
                unit = Utf8(unit),
                flags = ffmpeg.AV_OPT_FLAG_ENCODING_PARAM,
            };
        try
        {
            options[0] = Option("quality", AVOptionType.AV_OPT_TYPE_INT, -1, 63);
            options[1] = Option("deadline", AVOptionType.AV_OPT_TYPE_INT64, 0, 2000000, "deadline");
            options[2] = Option("realtime", AVOptionType.AV_OPT_TYPE_CONST, 0, 0, "deadline");
            options[2].default_val.i64 = 1;
            options[3] = Option("profile", AVOptionType.AV_OPT_TYPE_STRING, 0, 0);
            options[4] = Option("ratio", AVOptionType.AV_OPT_TYPE_DOUBLE, 0, double.PositiveInfinity);
            options[5] = Option("enabled", AVOptionType.AV_OPT_TYPE_BOOL, -1, 1);
            options[6] = Option("flags", AVOptionType.AV_OPT_TYPE_FLAGS, 0, 100, "flags");
            options[7] = Option("partitions", AVOptionType.AV_OPT_TYPE_CONST, 0, 0, "flags");
            options[7].default_val.i64 = 2;
            options[8] = Option("readonly", AVOptionType.AV_OPT_TYPE_INT, 0, 1);
            options[8].flags |= ffmpeg.AV_OPT_FLAG_READONLY;
            options[9] = Option("decoder-only", AVOptionType.AV_OPT_TYPE_INT, 0, 1);
            options[9].flags = ffmpeg.AV_OPT_FLAG_DECODING_PARAM;
            options[10] = Option(null, AVOptionType.AV_OPT_TYPE_INT, 0, 1);
            context->priv_data = options;
            vectors.avcodec_alloc_context3 = _ => context;
            vectors.avcodec_free_context = pointer => { releasedContexts++; *pointer = null; };
            vectors.av_opt_next = (_, previous) => previous == null ? options : previous < options + 10 ? previous + 1 : null;
            vectors.av_opt_get = (void* obj, string name, int flags, byte** value) =>
            {
                *value = defaults.TryGetValue(name, out string? text) ? (byte*)Marshal.StringToCoTaskMemUTF8(text) : null;
                return *value != null ? 0 : -22;
            };
            vectors.av_free = pointer => { releasedValues++; Marshal.FreeCoTaskMem((nint)pointer); };

            var codec = new MediaCodec(nint.Zero);
            EncoderOptionInfo[] result = EncoderOptionsQuery.GetOptions(codec, AVPixelFormat.AV_PIX_FMT_YUV420P10LE);
            Assert.That(context->pix_fmt, Is.EqualTo(AVPixelFormat.AV_PIX_FMT_YUV420P10LE));
            Assert.That(result.Select(o => o.Name), Is.EqualTo(new[] { "quality", "deadline", "profile", "ratio", "enabled", "flags" }));
            EncoderOptionInfo quality = result.Single(o => o.Name == "quality");
            Assert.That(quality.Kind, Is.EqualTo(EncoderOptionKind.Integer));
            Assert.That(quality.Description, Is.EqualTo("説明"));
            Assert.That(quality.Minimum, Is.EqualTo(-1));
            Assert.That(quality.Maximum, Is.EqualTo(63));
            Assert.That(quality.DefaultValue, Is.EqualTo("22"));
            EncoderOptionInfo deadline = result.Single(o => o.Name == "deadline");
            Assert.That(deadline.Kind, Is.EqualTo(EncoderOptionKind.Choice));
            Assert.That(deadline.AllowsNumericValues, Is.True);
            Assert.That(deadline.RequiresInteger, Is.True);
            Assert.That(deadline.Choices.Single().Value, Is.EqualTo("realtime"));
            Assert.That(deadline.Choices.Single().NumericValue, Is.EqualTo(1));
            Assert.That(result.Single(o => o.Name == "profile").Choices, Is.Empty);
            Assert.That(result.Single(o => o.Name == "profile").Minimum, Is.Null);
            Assert.That(result.Single(o => o.Name == "ratio").Kind, Is.EqualTo(EncoderOptionKind.Number));
            Assert.That(result.Single(o => o.Name == "ratio").Maximum, Is.Null);
            Assert.That(result.Single(o => o.Name == "enabled").Kind, Is.EqualTo(EncoderOptionKind.Boolean));
            Assert.That(result.Single(o => o.Name == "flags").Kind, Is.EqualTo(EncoderOptionKind.Text));
            Assert.That(result.Single(o => o.Name == "flags").Choices.Single().Value, Is.EqualTo("partitions"));
            Assert.That(releasedContexts, Is.EqualTo(1));
            Assert.That(releasedValues, Is.EqualTo(result.Length));

            defaults.Remove("quality");
            Assert.That(EncoderOptionsQuery.GetOptions(codec, AVPixelFormat.AV_PIX_FMT_NONE).Single(o => o.Name == "quality").DefaultValue, Is.Null);
            context->priv_data = null;
            Assert.That(EncoderOptionsQuery.GetOptions(codec, AVPixelFormat.AV_PIX_FMT_NONE), Is.Empty);
            Assert.That(releasedContexts, Is.EqualTo(3));
            vectors.avcodec_alloc_context3 = _ => null;
            Assert.Throws<OutOfMemoryException>(() => EncoderOptionsQuery.GetOptions(codec, AVPixelFormat.AV_PIX_FMT_NONE));
        }
        finally
        {
            vectors.avcodec_alloc_context3 = allocate;
            vectors.avcodec_free_context = release;
            vectors.av_opt_next = next;
            vectors.av_opt_get = get;
            vectors.av_free = free;
            foreach (nint pointer in strings) Marshal.FreeCoTaskMem(pointer);
            NativeMemory.Free(options);
            NativeMemory.Free(context);
        }
    }
}
