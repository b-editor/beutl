using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Pixel;
using MonoMac.AVFoundation;
using MonoMac.CoreGraphics;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation.Decoding;

public unsafe sealed class AVFReader : MediaReader
{
    private readonly AVAsset _asset;
    private readonly AVPlayerItem _playerItem;
    private readonly AVPlayerItemVideoOutput _videoOutput;
    private readonly AVPlayer _player;
    private readonly AVAssetTrack _videoTrack;
    private string _file;
    private MediaOptions _options;
    private AVFDecodingExtension _extension;

    public AVFReader(string file, MediaOptions options, AVFDecodingExtension extension)
    {
        _file = file;
        _options = options;
        _extension = extension;
        var url = NSUrl.FromFilename(file);
        _asset = AVAsset.FromUrl(url);
        _playerItem = AVPlayerItem.FromAsset(_asset);
        _videoOutput = new AVPlayerItemVideoOutput();
        // _videoOutput.Delegate= new AVPlayerItemOutputPullDelegate()


        _playerItem.AddOutput(_videoOutput);

        _player = new AVPlayer(_playerItem);

        _videoTrack = _asset.TracksWithMediaType(AVMediaType.Video)[0];

        var fmtdesc = _videoTrack.FormatDescriptions[0];
        var frameSize = new PixelSize(fmtdesc.VideoDimensions.Width, fmtdesc.VideoDimensions.Height);
        var codec = fmtdesc.VideoCodecType.ToString();
        var framerate = _videoTrack.NominalFrameRate;
        var duration = _videoTrack.TotalSampleDataLength / _videoTrack.EstimatedDataRate * 8d;
        VideoInfo = new VideoStreamInfo(
            codec,
            Rational.FromDouble(duration),
            frameSize,
            Rational.FromSingle(framerate));
    }

    public override VideoStreamInfo VideoInfo { get; }

    public override AudioStreamInfo AudioInfo => throw new NotImplementedException();

    public override bool HasVideo => true;

    public override bool HasAudio => false;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        throw new NotImplementedException();
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        image = null;
        _player.Seek(CMTime.FromSeconds(frame / (double)_videoTrack.NominalFrameRate, 1))
        // _assetReader.TimeRange = new CMTimeRange
        // {
        //     Start = CMTime.FromSeconds(frame / (double)_videoTrack.NominalFrameRate, 1),
        //     Duration = CMTime.PositiveInfinity
        // };
        _videoOutput.

        using var buffer = _videoReader.CopyNextSampleBuffer();
        if (buffer.DataIsReady && buffer.IsValid)
        {
            using var imgbuf = buffer.GetImageBuffer();
            var d = buffer.GetDataBuffer();
            // CMBlockBuffer
            if (imgbuf is CVPixelBuffer pixbuf)
            {
                var r = pixbuf.Lock(CVOptionFlags.None);
                if (r != CVReturn.Success) return false;

                var ptr = pixbuf.GetBaseAddress(0);
                var bytesPerRow = pixbuf.BytesPerRow;
                var width = pixbuf.Width;
                var height = pixbuf.Height;
                using CGColorSpace colorSpace = CGColorSpace.CreateDeviceRGB();

                using var newContext = new CGBitmapContext(
                    ptr, width, height,
                    8, bytesPerRow, colorSpace,
                    CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst);

                using CGImage cgimage = newContext.ToImage();
                using var data = cgimage.DataProvider.CopyData();

                var bitmap = new Bitmap<Bgra8888>(cgimage.Width, cgimage.Height);
                Debug.Assert(bitmap.ByteCount == (int)data.Length);
                Buffer.MemoryCopy((void*)data.Bytes, (void*)bitmap.Data, (long)data.Length, bitmap.ByteCount);
                Parallel.For(0, bitmap.DataSpan.Length, i =>
                {
                    ref var p = ref bitmap.DataSpan[i];
                    p.A = 255;
                });

                pixbuf.Unlock(CVOptionFlags.None);

                image = bitmap;
                return true;
            }
        }

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _asset.Dispose();
        _assetReader.Dispose();
        _videoReader.Dispose();
    }
}
