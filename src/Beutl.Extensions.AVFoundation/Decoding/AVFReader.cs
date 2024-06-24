using System.Diagnostics.CodeAnalysis;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using MonoMac.AVFoundation;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation.Decoding;

public sealed class AVFReader : MediaReader
{
    private readonly AVAsset _asset;

    private AVFVideoStreamReader? _videoReader;
    private AVFAudioStreamReader? _audioReader;

    public AVFReader(string file, MediaOptions options, AVFDecodingExtension extension)
    {
        var url = NSUrl.FromFilename(file);
        _asset = AVAsset.FromUrl(url);
        if (options.StreamsToLoad.HasFlag(MediaMode.Video))
        {
            _videoReader = new AVFVideoStreamReader(_asset, extension);
        }

        if (options.StreamsToLoad.HasFlag(MediaMode.Audio))
        {
            _audioReader = new AVFAudioStreamReader(_asset, options, extension);
        }
    }

    public override VideoStreamInfo VideoInfo =>
        _videoReader?.VideoInfo ?? throw new Exception("VideoInfo is not available.");

    public override AudioStreamInfo AudioInfo =>
        _audioReader?.AudioInfo ?? throw new Exception("AudioInfo is not available.");

    public override bool HasVideo => _videoReader != null;

    public override bool HasAudio => _audioReader != null;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        if (_audioReader != null)
        {
            return _audioReader.ReadAudio(start, length, out sound);
        }

        sound = null;
        return false;
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        if (_videoReader != null)
        {
            return _videoReader.ReadVideo(frame, out image);
        }

        image = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _audioReader?.Dispose();
        _videoReader?.Dispose();
        _asset.Dispose();

        _audioReader = null;
        _videoReader = null;
    }
}
