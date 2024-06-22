using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using MonoMac.AudioToolbox;
using MonoMac.AVFoundation;
using MonoMac.CoreFoundation;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation.Encoding;

public class AVFWriter : MediaWriter
{
    private readonly AVAssetWriter _assetWriter;
    private readonly AVAssetWriterInput _videoInput;
    private readonly AVAssetWriterInputPixelBufferAdaptor _videoAdaptor;
    private long _numberOfFrames;
    private readonly AVAssetWriterInput _audioInput;
    private readonly AudioSettings _audioSettings;
    private AudioConverter? _audioConverter;
    private AudioStreamBasicDescription? _audioSourceFormat;
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

        _assetWriter.AddInput(_videoInput);

        _audioSettings = new AudioSettings
        {
            SampleRate = audioConfig.SampleRate,
            EncoderBitRate = audioConfig.Bitrate == -1 ? null : audioConfig.Bitrate,
            NumberChannels = audioConfig.Channels,
            LinearPcmFloat = audioConfig.LinearPcmFloat,
            LinearPcmBigEndian = audioConfig.LinearPcmBigEndian,
            LinearPcmBitDepth = (int?)audioConfig.LinearPcmBitDepth,
            LinearPcmNonInterleaved = audioConfig.LinearPcmNonInterleaved,
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
        _audioInput = AVAssetWriterInput.Create(AVMediaType.Audio, _audioSettings);
        _assetWriter.AddInput(_audioInput);
        _audioInput.ExpectsMediaDataInRealTime = true;

        _videoAdaptor = AVAssetWriterInputPixelBufferAdaptor.Create(_videoInput,
            new CVPixelBufferAttributes
            {
                PixelFormatType = CVPixelFormatType.CV32ARGB,
                Width = videoConfig.SourceSize.Width,
                Height = videoConfig.SourceSize.Width,
            });
        _videoInput.ExpectsMediaDataInRealTime = true;

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
        if (!_videoAdaptor.AssetWriterInput.ReadyForMoreMediaData)
        {
            return false;
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
    private static extern CMBlockBufferError CMBlockBufferReplaceDataBytes(
        IntPtr sourceBytes,
        IntPtr handle,
        uint offsetIntoDestination,
        uint dataLength);

    [DllImport("/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox")]
    private static unsafe extern AudioConverterError AudioConverterConvertBuffer(
        IntPtr handle,
        uint inInputDataSize, IntPtr inInputData,
        uint* ioOutputDataSize, IntPtr outOutputData);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "handle")]
    private static extern IntPtr GetHandle(AudioConverter self);

    public override unsafe bool AddAudio(IPcm sound)
    {
        if (!_audioInput.ReadyForMoreMediaData)
        {
            return false;
        }

        var audioConfig = (AVFAudioEncoderSettings)AudioConfig;
        if (_audioConverter == null
            || !_audioSourceFormat.HasValue
            || (int)_audioSourceFormat.Value.SampleRate != sound.SampleRate
            || _audioSourceFormat.Value.BitsPerChannel != GetBits()
            || _audioSourceFormat.Value.ChannelsPerFrame != sound.NumChannels)
        {
            var sourceFormat = AudioStreamBasicDescription.CreateLinearPCM(sound.SampleRate, (uint)sound.NumChannels);
            sourceFormat.FormatFlags = GetFormatFlags();
            sourceFormat.BitsPerChannel = GetBits();
            _audioSourceFormat = sourceFormat;

            var destinationFormat =
                AudioStreamBasicDescription.CreateLinearPCM(AudioConfig.SampleRate, (uint)AudioConfig.Channels,
                    (uint)audioConfig.LinearPcmBitDepth, audioConfig.LinearPcmBigEndian);
            destinationFormat.FormatFlags =
                (audioConfig.LinearPcmFloat ? AudioFormatFlags.IsFloat : AudioFormatFlags.IsSignedInteger) |
                AudioFormatFlags.IsPacked;

            _audioConverter?.Dispose();
            _audioConverter = AudioConverter.Create(_audioSourceFormat.Value, destinationFormat);
        }

        uint inputDataSize = (uint)(sound.SampleSize * sound.NumSamples * sound.NumChannels);
        uint bytes = (uint)audioConfig.LinearPcmBitDepth / 8;
        uint outputSamples = (uint)Math.Ceiling(AudioConfig.SampleRate * sound.NumSamples / (double)sound.SampleRate);
        uint outputDataSize = bytes * outputSamples * (uint)AudioConfig.Channels;
        var outputData = NativeMemory.Alloc(outputDataSize);

        AudioConverterConvertBuffer(
            GetHandle(_audioConverter),
            inputDataSize, sound.Data,
            &outputDataSize, (IntPtr)outputData);
        Debug.Assert(outputDataSize == bytes * outputSamples * (uint)AudioConfig.Channels);

        var time = new CMTime(_numberOfSamples, AudioConfig.SampleRate);
        using var dataBuffer = CMBlockBuffer.CreateEmpty(
            outputDataSize,
            CMBlockBufferFlags.AlwaysCopyData, out var error1);
        if (error1 != CMBlockBufferError.None) throw new Exception(error1.ToString());

        var error2 = CMBlockBufferReplaceDataBytes((IntPtr)outputData, dataBuffer.Handle, 0, dataBuffer.DataLength);
        if (error2 != CMBlockBufferError.None) throw new Exception(error2.ToString());

        using var formatDescription =
            CMFormatDescription.Create(CMMediaType.Audio, (uint)AudioFormatType.LinearPCM, out var error3);
        if (error3 != CMFormatDescriptionError.None) throw new Exception(error3.ToString());

        using var sampleBuffer = CMSampleBuffer.CreateWithPacketDescriptions(dataBuffer, formatDescription,
            (int)outputSamples, time, null, out var error4);
        if (error4 != CMSampleBufferError.None) throw new Exception(error4.ToString());

        if (!_audioInput.AppendSampleBuffer(sampleBuffer))
        {
            return false;
        }

        _numberOfSamples += outputSamples;
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
                Pcm<Stereo32BitFloat> => AudioFormatFlags.IsSignedInteger | AudioFormatFlags.IsPacked,
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
