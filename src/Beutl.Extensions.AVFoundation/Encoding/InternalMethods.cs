using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MonoMac.AudioToolbox;
using MonoMac.CoreMedia;

namespace Beutl.Extensions.AVFoundation.Encoding;

public class InternalMethods
{
    public static unsafe CMAudioFormatDescription CreateAudioFormatDescription(AudioStreamBasicDescription asbd)
    {
        var channelLayout = new AudioChannelLayout
        {
            AudioTag = asbd.ChannelsPerFrame == 2 ? AudioChannelLayoutTag.Stereo : AudioChannelLayoutTag.Mono,
            Channels = [],
            Bitmap = 0,
        };
        var data = channelLayout.AsData();

        var error = CMAudioFormatDescriptionCreate(
            IntPtr.Zero,
            &asbd,
            (uint)data.Length,
            (void*)data.Bytes,
            0,
            null,
            IntPtr.Zero,
            out var handle);
        if (error != CMFormatDescriptionError.None) throw new Exception(error.ToString());
        return NewCMAudioFormatDescription(handle);
    }

    public static CMBlockBuffer CreateCMBlockBufferWithMemoryBlock(uint length, IntPtr memoryBlock,
        CMBlockBufferFlags flags)
    {
        var error = CMBlockBufferCreateWithMemoryBlock(
            IntPtr.Zero,
            memoryBlock,
            length,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            length,
            flags,
            out var handle);
        if (error != CMBlockBufferError.None) throw new Exception(error.ToString());
        return NewCMBlockBuffer(handle);
    }

    [DllImport("/System/Library/PrivateFrameworks/CoreMedia.framework/Versions/A/CoreMedia")]
    public static extern unsafe CMFormatDescriptionError CMAudioFormatDescriptionCreate(
        IntPtr allocator,
        void* asbd,
        uint layoutSize,
        void* layout,
        uint magicCookieSize,
        void* magicCookie,
        IntPtr extensions,
        out IntPtr handle);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public  static extern CMAudioFormatDescription NewCMAudioFormatDescription(IntPtr handle);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public  static extern CMBlockBuffer NewCMBlockBuffer(IntPtr handle);

    [DllImport("/System/Library/PrivateFrameworks/CoreMedia.framework/Versions/A/CoreMedia")]
    public  static extern CMBlockBufferError CMBlockBufferCreateWithMemoryBlock(
        IntPtr allocator,
        IntPtr memoryBlock,
        uint blockLength,
        IntPtr blockAllocator,
        IntPtr customBlockSource,
        uint offsetToData,
        uint dataLength,
        CMBlockBufferFlags flags,
        out IntPtr handle);
}
