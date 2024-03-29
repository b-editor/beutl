using FFMpegCore.Pipes;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal sealed class CustomRawAudioPipeSource(IEnumerable<IAudioSample> sampleEnumerator, Action<Stream> setstream) : IPipeSource
{
    private readonly IEnumerator<IAudioSample> _sampleEnumerator = sampleEnumerator.GetEnumerator();
    private readonly Action<Stream> _setstream = setstream;

    public string Format { get; set; } = "s16le";


    public uint SampleRate { get; set; } = 8000u;


    public uint Channels { get; set; } = 1u;

    public string GetStreamArguments()
    {
        return $"-f {Format} -ar {SampleRate} -ac {Channels}";
    }

    public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        _setstream?.Invoke(outputStream);

        if (_sampleEnumerator.Current != null)
        {
            await _sampleEnumerator.Current.SerializeAsync(outputStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        while (_sampleEnumerator.MoveNext())
        {
            await _sampleEnumerator.Current!.SerializeAsync(outputStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }
}
