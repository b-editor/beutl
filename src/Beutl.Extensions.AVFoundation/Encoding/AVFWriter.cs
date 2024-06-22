using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
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
            Codec = videoConfig.Codec switch
            {
                AVFVideoEncoderSettings.VideoCodec.H264 => AVVideoCodec.H264,
                AVFVideoEncoderSettings.VideoCodec.JPEG => AVVideoCodec.JPEG,
                _ => null,
            },
            CodecSettings = new AVVideoCodecSettings
            {
                AverageBitRate = videoConfig.Bitrate == -1 ? null : videoConfig.Bitrate,
                MaxKeyFrameInterval = videoConfig.KeyframeRate == -1 ? null : videoConfig.KeyframeRate,
                JPEGQuality = videoConfig.JPEGQuality < 0 ? null : videoConfig.JPEGQuality,
                ProfileLevelH264 = videoConfig.ProfileLevelH264 switch
                {
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline30 => AVVideoProfileLevelH264.Baseline30,
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline31 => AVVideoProfileLevelH264.Baseline31,
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Baseline41 => AVVideoProfileLevelH264.Baseline41,
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Main30 => AVVideoProfileLevelH264.Main30,
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Main31 => AVVideoProfileLevelH264.Main31,
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Main32 => AVVideoProfileLevelH264.Main32,
                    AVFVideoEncoderSettings.VideoProfileLevelH264.Main41 => AVVideoProfileLevelH264.Main41,
                    _ => null,
                },
            },
        });

        _assetWriter.AddInput(_videoInput);

        _audioSettings = new AudioSettings
        {
            SampleRate = audioConfig.SampleRate,
            EncoderBitRate = audioConfig.Bitrate == -1 ? null : audioConfig.Bitrate,
            NumberChannels = audioConfig.Channels,
            Format = audioConfig.Format switch
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
                _ => null,
            },
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

    public override bool AddAudio(IPcm sound)
    {
        // if (!_audioInput.ReadyForMoreMediaData)
        // {
        //     return false;
        // }
        //
        // var time = new CMTime(_numberOfSamples, AudioConfig.SampleRate);
        // using var dataBuffer = CMBlockBuffer.CreateEmpty(
        //     (uint)(sound.SampleSize * sound.NumSamples * sound.NumChannels),
        //     CMBlockBufferFlags.AlwaysCopyData, out var error1);
        // using var formatDescription =
        //     CMFormatDescription.Create(CMMediaType.Audio, (uint)AudioFormatType.LinearPCM, out var error2);
        // using var sampleBuffer = CMSampleBuffer.CreateWithPacketDescriptions(dataBuffer, formatDescription,
        //     sound.NumSamples, time, [], out var error3);
        //
        // sampleBuffer.
        // // _numberOfSamples
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _videoInput.MarkAsFinished();
        _assetWriter.EndSessionAtSourceTime(new CMTime(_numberOfFrames * VideoConfig.FrameRate.Denominator,
            (int)VideoConfig.FrameRate.Numerator));
        _assetWriter.FinishWriting();
    }
}
