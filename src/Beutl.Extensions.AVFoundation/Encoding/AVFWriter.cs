using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using MonoMac.AudioToolbox;
using MonoMac.AVFoundation;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation.Encoding;

[SupportedOSPlatform("macos")]
public class AVFWriter : MediaWriter
{
    private readonly AVAssetWriter _assetWriter;
    private readonly AVAssetWriterInput _videoInput;
    private readonly AVAssetWriterInputPixelBufferAdaptor _videoAdaptor;
    private long _numberOfFrames;
    private readonly AVAssetWriterInput _audioInput;
    private long _numberOfSamples;

    public AVFWriter(string file, AVFVideoEncoderSettings videoConfig, AVFAudioEncoderSettings audioConfig)
        : base(videoConfig, audioConfig)
    {
        var url = NSUrl.FromFilename(file);
        _assetWriter = AVAssetWriter.FromUrl(url, AVFileType.Mpeg4, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);

        _videoInput = AVAssetWriterInput.Create(AVMediaType.Video, new AVVideoSettingsCompressed
        {
            Width = videoConfig.DestinationSize.Width,
            Height = videoConfig.DestinationSize.Height,
            Codec = ToAVVideoCodec(videoConfig.Codec),
            CodecSettings = new AVVideoCodecSettings
            {
                AverageBitRate = videoConfig.Bitrate == -1 ? null : videoConfig.Bitrate,
                MaxKeyFrameInterval = videoConfig.KeyframeRate == -1 ? null : videoConfig.KeyframeRate,
                JPEGQuality = videoConfig.JPEGQuality < 0 ? null : videoConfig.JPEGQuality,
                ProfileLevelH264 = ToAVVideoProfileLevelH264(videoConfig.ProfileLevelH264),
            },
        });
        _videoInput.ExpectsMediaDataInRealTime = true;
        _videoAdaptor = AVAssetWriterInputPixelBufferAdaptor.Create(_videoInput,
            new CVPixelBufferAttributes
            {
                PixelFormatType = CVPixelFormatType.CV32ARGB,
                Width = videoConfig.SourceSize.Width,
                Height = videoConfig.SourceSize.Width,
            });
        _assetWriter.AddInput(_videoInput);

        var audioSettings = new AudioSettings
        {
            SampleRate = audioConfig.SampleRate,
            EncoderBitRate = audioConfig.Bitrate == -1 ? null : audioConfig.Bitrate,
            NumberChannels = audioConfig.Channels,
            Format = ToAudioFormatType(audioConfig.Format),
            AudioQuality =
                audioConfig.Quality == AVFAudioEncoderSettings.AudioQuality.Default
                    ? null
                    : (AVAudioQuality?)audioConfig.Quality,
            SampleRateConverterAudioQuality =
                audioConfig.SampleRateConverterQuality == AVFAudioEncoderSettings.AudioQuality.Default
                    ? null
                    : (AVAudioQuality?)audioConfig.SampleRateConverterQuality,
        };
        if (audioSettings.Format == AudioFormatType.LinearPCM)
        {
            audioSettings.LinearPcmFloat = audioConfig.LinearPcmFloat;
            audioSettings.LinearPcmBigEndian = audioConfig.LinearPcmBigEndian;
            audioSettings.LinearPcmBitDepth = (int?)audioConfig.LinearPcmBitDepth;
            audioSettings.LinearPcmNonInterleaved = audioConfig.LinearPcmNonInterleaved;
        }

        _audioInput = AVAssetWriterInput.Create(AVMediaType.Audio, audioSettings);
        _audioInput.ExpectsMediaDataInRealTime = true;
        _assetWriter.AddInput(_audioInput);

        if (!_assetWriter.StartWriting())
        {
            throw new Exception("Failed to start writing");
        }

        _assetWriter.StartSessionAtSourceTime(CMTime.Zero);
    }

    public override long NumberOfFrames => _numberOfFrames;

    public override long NumberOfSamples => _numberOfSamples;

    public override bool AddVideo(IBitmap image)
    {
        int count = 0;
        while (!_videoAdaptor.AssetWriterInput.ReadyForMoreMediaData)
        {
            Thread.Sleep(10);
            count++;
            if (count > 100)
            {
                return false;
            }
        }

        var time = new CMTime(_numberOfFrames * VideoConfig.FrameRate.Denominator,
            (int)VideoConfig.FrameRate.Numerator);
        CVPixelBuffer? pixelBuffer;
        if (image is Bitmap<Bgra8888> bgra8888)
        {
            pixelBuffer = AVFSampleUtilities.ConvertToCVPixelBuffer(bgra8888);
        }
        else
        {
            using var copy = image.Convert<Bgra8888>();
            pixelBuffer = AVFSampleUtilities.ConvertToCVPixelBuffer(copy);
        }

        if (pixelBuffer == null)
        {
            return false;
        }

        if (!_videoAdaptor.AppendPixelBufferWithPresentationTime(pixelBuffer, time))
        {
            return false;
        }

        _numberOfFrames++;
        return true;
    }

    [DllImport("/System/Library/PrivateFrameworks/CoreMedia.framework/Versions/A/CoreMedia")]
    private static extern unsafe CMFormatDescriptionError CMAudioFormatDescriptionCreate(
        IntPtr allocator,
        void* asbd,
        uint layoutSize,
        void* layout,
        uint magicCookieSize,
        void* magicCookie,
        IntPtr extensions,
        out IntPtr handle);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern CMAudioFormatDescription NewCMAudioFormatDescription(IntPtr handle);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    private static extern CMBlockBuffer NewCMBlockBuffer(IntPtr handle);

    [DllImport("/System/Library/PrivateFrameworks/CoreMedia.framework/Versions/A/CoreMedia")]
    private static extern CMBlockBufferError CMBlockBufferCreateWithMemoryBlock(
        IntPtr allocator,
        IntPtr memoryBlock,
        uint blockLength,
        IntPtr blockAllocator,
        IntPtr customBlockSource,
        uint offsetToData,
        uint dataLength,
        CMBlockBufferFlags flags,
        out IntPtr handle);

    private static unsafe CMAudioFormatDescription CreateAudioFormatDescription(AudioStreamBasicDescription asbd)
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

    private static CMBlockBuffer CreateCMBlockBufferWithMemoryBlock(uint length, IntPtr memoryBlock,
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

    public override bool AddAudio(IPcm sound)
    {
        int count = 0;
        while (!_audioInput.ReadyForMoreMediaData)
        {
            Thread.Sleep(10);
            count++;
            if (count > 100)
            {
                return false;
            }
        }

        var sourceFormat = AudioStreamBasicDescription.CreateLinearPCM(sound.SampleRate, (uint)sound.NumChannels);
        sourceFormat.FormatFlags = GetFormatFlags();
        sourceFormat.BitsPerChannel = GetBits();
        var fmtError = AudioStreamBasicDescription.GetFormatInfo(ref sourceFormat);
        if (fmtError != AudioFormatError.None) throw new Exception(fmtError.ToString());

        uint inputDataSize = (uint)(sound.SampleSize * sound.NumSamples);
        var time = new CMTime(_numberOfSamples, sound.SampleRate);
        var dataBuffer = CreateCMBlockBufferWithMemoryBlock(
            inputDataSize, sound.Data, CMBlockBufferFlags.AlwaysCopyData);

        var formatDescription = CreateAudioFormatDescription(sourceFormat);

        var sampleBuffer = CMSampleBuffer.CreateWithPacketDescriptions(dataBuffer, formatDescription,
            sound.NumSamples, time, null, out var error4);
        if (error4 != CMSampleBufferError.None) throw new Exception(error4.ToString());

        if (!_audioInput.AppendSampleBuffer(sampleBuffer))
        {
            return false;
        }

        _numberOfSamples += sound.NumSamples;
        return true;

        int GetBits()
        {
            return sound switch
            {
                Pcm<Stereo32BitFloat> or Pcm<Stereo32BitInteger> => 32,
                Pcm<Stereo16BitInteger> => 16,
                _ => throw new NotSupportedException()
            };
        }

        AudioFormatFlags GetFormatFlags()
        {
            return sound switch
            {
                Pcm<Stereo32BitFloat> => AudioFormatFlags.IsFloat | AudioFormatFlags.IsPacked,
                Pcm<Stereo16BitInteger> or Pcm<Stereo32BitInteger> => AudioFormatFlags.IsSignedInteger |
                                                                      AudioFormatFlags.IsPacked,
                _ => throw new NotSupportedException()
            };
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _videoInput.MarkAsFinished();
        _audioInput.MarkAsFinished();
        _assetWriter.EndSessionAtSourceTime(new CMTime(_numberOfFrames * VideoConfig.FrameRate.Denominator,
            (int)VideoConfig.FrameRate.Numerator));
        _assetWriter.FinishWriting();
    }

    private AVVideoCodec? ToAVVideoCodec(AVFVideoEncoderSettings.VideoCodec codec)
    {
        return codec switch
        {
            AVFVideoEncoderSettings.VideoCodec.H264 => AVVideoCodec.H264,
            AVFVideoEncoderSettings.VideoCodec.JPEG => AVVideoCodec.JPEG,
            _ => null
        };
    }

    private AVVideoProfileLevelH264? ToAVVideoProfileLevelH264(AVFVideoEncoderSettings.VideoProfileLevelH264 profile)
    {
        return profile switch
        {
            AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline30 => AVVideoProfileLevelH264.Baseline30,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline31 => AVVideoProfileLevelH264.Baseline31,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline41 => AVVideoProfileLevelH264.Baseline41,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main30 => AVVideoProfileLevelH264.Main30,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main31 => AVVideoProfileLevelH264.Main31,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main32 => AVVideoProfileLevelH264.Main32,
            AVFVideoEncoderSettings.VideoProfileLevelH264.Main41 => AVVideoProfileLevelH264.Main41,
            _ => null
        };
    }

    private AudioFormatType? ToAudioFormatType(AVFAudioEncoderSettings.AudioFormatType format)
    {
        return format switch
        {
            AVFAudioEncoderSettings.AudioFormatType.MPEGLayer1 => AudioFormatType.MPEGLayer1,
            AVFAudioEncoderSettings.AudioFormatType.MPEGLayer2 => AudioFormatType.MPEGLayer2,
            AVFAudioEncoderSettings.AudioFormatType.MPEGLayer3 => AudioFormatType.MPEGLayer3,
            AVFAudioEncoderSettings.AudioFormatType.Audible => AudioFormatType.Audible,
            AVFAudioEncoderSettings.AudioFormatType.MACE3 => AudioFormatType.MACE3,
            AVFAudioEncoderSettings.AudioFormatType.MACE6 => AudioFormatType.MACE6,
            AVFAudioEncoderSettings.AudioFormatType.QDesign2 => AudioFormatType.QDesign2,
            AVFAudioEncoderSettings.AudioFormatType.QDesign => AudioFormatType.QDesign,
            AVFAudioEncoderSettings.AudioFormatType.QUALCOMM => AudioFormatType.QUALCOMM,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC => AudioFormatType.MPEG4AAC,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_ELD => AudioFormatType.MPEG4AAC_ELD,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_ELD_SBR => AudioFormatType.MPEG4AAC_ELD_SBR,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_ELD_V2 => AudioFormatType.MPEG4AAC_ELD_V2,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_HE => AudioFormatType.MPEG4AAC_HE,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_LD => AudioFormatType.MPEG4AAC_LD,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_HE_V2 => AudioFormatType.MPEG4AAC_HE_V2,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4AAC_Spatial => AudioFormatType.MPEG4AAC_Spatial,
            AVFAudioEncoderSettings.AudioFormatType.AC3 => AudioFormatType.AC3,
            AVFAudioEncoderSettings.AudioFormatType.AES3 => AudioFormatType.AES3,
            AVFAudioEncoderSettings.AudioFormatType.AppleLossless => AudioFormatType.AppleLossless,
            AVFAudioEncoderSettings.AudioFormatType.ALaw => AudioFormatType.ALaw,
            AVFAudioEncoderSettings.AudioFormatType.ParameterValueStream => AudioFormatType.ParameterValueStream,
            AVFAudioEncoderSettings.AudioFormatType.CAC3 => AudioFormatType.CAC3,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4CELP => AudioFormatType.MPEG4CELP,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4HVXC => AudioFormatType.MPEG4HVXC,
            AVFAudioEncoderSettings.AudioFormatType.iLBC => AudioFormatType.iLBC,
            AVFAudioEncoderSettings.AudioFormatType.AppleIMA4 => AudioFormatType.AppleIMA4,
            AVFAudioEncoderSettings.AudioFormatType.LinearPCM => AudioFormatType.LinearPCM,
            AVFAudioEncoderSettings.AudioFormatType.MIDIStream => AudioFormatType.MIDIStream,
            AVFAudioEncoderSettings.AudioFormatType.DVIIntelIMA => AudioFormatType.DVIIntelIMA,
            AVFAudioEncoderSettings.AudioFormatType.MicrosoftGSM => AudioFormatType.MicrosoftGSM,
            AVFAudioEncoderSettings.AudioFormatType.AMR => AudioFormatType.AMR,
            AVFAudioEncoderSettings.AudioFormatType.TimeCode => AudioFormatType.TimeCode,
            AVFAudioEncoderSettings.AudioFormatType.MPEG4TwinVQ => AudioFormatType.MPEG4TwinVQ,
            AVFAudioEncoderSettings.AudioFormatType.ULaw => AudioFormatType.ULaw,
            _ => null
        };
    }
}
