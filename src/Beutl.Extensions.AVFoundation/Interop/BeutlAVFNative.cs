using System.Runtime.InteropServices;
using SystemEncoding = System.Text.Encoding;

namespace Beutl.Extensions.AVFoundation.Interop;

internal static partial class BeutlAVFNative
{
    internal const string DllName = "BeutlAVF";

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_version();

    [LibraryImport(DllName)]
    internal static partial void beutl_avf_last_error_message(IntPtr buffer, nuint capacity);

    internal static string GetLastErrorMessage()
    {
        const int Capacity = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(Capacity);
        try
        {
            beutl_avf_last_error_message(buffer, (nuint)Capacity);
            return Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ---- Reader ----

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int beutl_avf_reader_open(
        string path,
        int modeFlags,
        ref BeutlReaderOptions options,
        out AVFReaderSafeHandle outHandle);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_reader_has_video(AVFReaderSafeHandle handle, out int outHasVideo);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_reader_has_audio(AVFReaderSafeHandle handle, out int outHasAudio);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_reader_get_video_info(AVFReaderSafeHandle handle, out BeutlVideoInfo outInfo);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_reader_get_audio_info(AVFReaderSafeHandle handle, out BeutlAudioInfo outInfo);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_reader_read_video(
        AVFReaderSafeHandle handle,
        long frameIndex,
        IntPtr outBuffer,
        int capacityBytes,
        int rowBytes);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_reader_read_audio(
        AVFReaderSafeHandle handle,
        long startSample,
        int lengthSamples,
        IntPtr outBuffer,
        int capacityBytes);

    // Used by SafeHandle.ReleaseHandle; operates on the raw IntPtr after the SafeHandle has flipped to invalid.
    [LibraryImport(DllName)]
    internal static partial void beutl_avf_reader_close(IntPtr handle);

    // ---- Writer ----

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int beutl_avf_writer_create(
        string path,
        ref BeutlVideoEncoderConfig videoConfig,
        ref BeutlAudioEncoderConfig audioConfig,
        out AVFWriterSafeHandle outHandle);

    // Overloads for the optional-config case: we can't pass `null` through a managed `ref`,
    // so Swift's nullable C pointers are expressed via separate IntPtr-based P/Invokes.
    [LibraryImport(DllName, EntryPoint = "beutl_avf_writer_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int beutl_avf_writer_create_raw(
        string path,
        IntPtr videoConfig,
        IntPtr audioConfig,
        out AVFWriterSafeHandle outHandle);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_writer_start(AVFWriterSafeHandle handle);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_writer_append_video(
        AVFWriterSafeHandle handle,
        IntPtr bgra,
        int width,
        int height,
        int rowBytes,
        long ptsNum,
        int ptsDen);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_writer_append_audio(
        AVFWriterSafeHandle handle,
        IntPtr pcm,
        int numSamples,
        long ptsSamples,
        int sampleRate);

    [LibraryImport(DllName)]
    internal static partial int beutl_avf_writer_finish(AVFWriterSafeHandle handle);

    [LibraryImport(DllName)]
    internal static partial void beutl_avf_writer_close(IntPtr handle);

    internal static string FourCCToString(int fourCC)
    {
        if (fourCC == 0) return string.Empty;
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)((fourCC >> 24) & 0xFF);
        bytes[1] = (byte)((fourCC >> 16) & 0xFF);
        bytes[2] = (byte)((fourCC >> 8) & 0xFF);
        bytes[3] = (byte)(fourCC & 0xFF);
        return SystemEncoding.ASCII.GetString(bytes).Trim('\0', ' ');
    }
}
