using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Beutl.Audio.Platforms.AVFoundation;

[SupportedOSPlatform("macos")]
internal static unsafe class AVFoundationNative
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    static AVFoundationNative()
    {
        // Register AVFoundation's Objective-C classes before the first objc_getClass call.
        // Keep the framework loaded for the lifetime of the process.
        NativeLibrary.Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
    }

    internal static readonly nint s_alloc = sel_registerName("alloc");
    internal static readonly nint s_init = sel_registerName("init");
    internal static readonly nint s_release = sel_registerName("release");
    internal static readonly nint s_addRenderer = sel_registerName("addRenderer:");
    internal static readonly nint s_setRateTime = sel_registerName("setRate:time:");
    internal static readonly nint s_timebase = sel_registerName("timebase");
    internal static readonly nint s_ready = sel_registerName("isReadyForMoreMediaData");
    internal static readonly nint s_enqueue = sel_registerName("enqueueSampleBuffer:");
    internal static readonly nint s_flush = sel_registerName("flush");
    internal static readonly nint s_status = sel_registerName("status");
    internal static readonly nint s_error = sel_registerName("error");
    internal static readonly nint s_localizedDescription = sel_registerName("localizedDescription");
    internal static readonly nint s_utf8String = sel_registerName("UTF8String");

    [DllImport(ObjC)]
    internal static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // Each signature is declared separately: in particular BOOL is one byte, NSInteger is
    // pointer-sized, and setRate:time: passes a float and the entire CMTime by value.
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static extern nint SendIntPtr(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static extern byte SendBool(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static extern void SendVoid(nint receiver, nint selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static extern void SendObject(nint receiver, nint selector, nint argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    internal static extern void SendRateTime(nint receiver, nint selector, float rate, CMTime time);

    [DllImport(ObjC)]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(nint pool);

    // Managed playback continuations may run on threads without an Objective-C autorelease pool.
    internal readonly struct AutoreleasePool() : IDisposable
    {
        private readonly nint _pool = objc_autoreleasePoolPush();

        public void Dispose() => objc_autoreleasePoolPop(_pool);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMTime(long value, int timescale)
    {
        public long Value = value;
        public int Timescale = timescale;
        public uint Flags = 1; // kCMTimeFlags_Valid
        public long Epoch = 0;

        public readonly double Seconds => (Flags & 0x1d) == 1 && Timescale > 0
            ? (double)Value / Timescale
            : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    // Native sizeof(AudioChannelLayout) includes one unused AudioChannelDescription (20 bytes).
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    internal struct ChannelLayout
    {
        public uint Tag;
        public uint Bitmap;
        public uint NumberChannelDescriptions;
    }

    [DllImport(CoreMedia)]
    internal static extern int CMAudioFormatDescriptionCreate(nint allocator,
        in AudioStreamBasicDescription asbd, nuint layoutSize, in ChannelLayout layout,
        nuint magicCookieSize, nint magicCookie, nint extensions, out nint description);

    [DllImport(CoreMedia)]
    internal static extern AudioStreamBasicDescription* CMAudioFormatDescriptionGetStreamBasicDescription(nint description);

    [DllImport(CoreMedia)]
    internal static extern ChannelLayout* CMAudioFormatDescriptionGetChannelLayout(nint description, out nuint size);

    [DllImport(CoreMedia)]
    internal static extern int CMBlockBufferCreateWithMemoryBlock(nint allocator, nint memoryBlock,
        nuint blockLength, nint blockAllocator, nint customBlockSource, nuint offsetToData,
        nuint dataLength, uint flags, out nint blockBuffer);

    [DllImport(CoreMedia)]
    internal static extern int CMBlockBufferGetDataPointer(nint blockBuffer, nuint offset,
        out nuint lengthAtOffset, out nuint totalLength, out nint data);

    [DllImport(CoreMedia)]
    internal static extern int CMAudioSampleBufferCreateReadyWithPacketDescriptions(nint allocator,
        nint dataBuffer, nint formatDescription, nint sampleCount, CMTime presentationTimeStamp,
        nint packetDescriptions, out nint sampleBuffer);

    [DllImport(CoreMedia)]
    internal static extern nint CMSampleBufferGetNumSamples(nint sampleBuffer);

    [DllImport(CoreMedia)]
    internal static extern CMTime CMSampleBufferGetPresentationTimeStamp(nint sampleBuffer);

    [DllImport(CoreMedia)]
    internal static extern nint CMSampleBufferGetDataBuffer(nint sampleBuffer);

    [DllImport(CoreMedia)]
    internal static extern CMTime CMTimebaseGetTime(nint timebase);

    [DllImport(CoreFoundation)]
    internal static extern void CFRelease(nint value);

    internal static void Check(int status, string operation)
    {
        if (status != 0)
            throw new AVSampleBufferAudioRendererException($"{operation} failed (OSStatus {status}).");
    }
}
