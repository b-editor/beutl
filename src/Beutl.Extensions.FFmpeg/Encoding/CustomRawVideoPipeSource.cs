using FFMpegCore.Exceptions;
using FFMpegCore.Pipes;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal sealed class CustomRawVideoPipeSource(IEnumerable<IVideoFrame> framesEnumerator, Action<Stream>? setstream) : IPipeSource
{
    private readonly IEnumerator<IVideoFrame> _framesEnumerator = framesEnumerator.GetEnumerator();
    private readonly Action<Stream>? _setstream = setstream;

    public required string StreamFormat { get; set; }

    public required int Width { get; set; }

    public required int Height { get; set; }

    public required Rational FrameRate { get; set; }

    public string GetStreamArguments()
    {
        return $"-f rawvideo -r {FrameRate.Numerator}/{FrameRate.Denominator} -pix_fmt {StreamFormat} -s {Width}x{Height}";
    }

    public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        _setstream?.Invoke(outputStream);
        if (_framesEnumerator.Current != null)
        {
            CheckFrameAndThrow(_framesEnumerator.Current);
            await _framesEnumerator.Current.SerializeAsync(outputStream, cancellationToken).ConfigureAwait(false);
        }

        while (_framesEnumerator.MoveNext())
        {
            CheckFrameAndThrow(_framesEnumerator.Current!);
            await _framesEnumerator.Current!.SerializeAsync(outputStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private void CheckFrameAndThrow(IVideoFrame frame)
    {
        if (frame.Width != Width || frame.Height != Height || frame.Format != StreamFormat)
        {
            throw new FFMpegStreamFormatException(FFMpegExceptionType.Operation, "Video frame is not the same format as created raw video stream\r\n" +
                $"Frame format: {frame.Width}x{frame.Height} pix_fmt: {frame.Format}\r\n" +
                $"Stream format: {Width}x{Height} pix_fmt: {StreamFormat}");
        }
    }
}
