using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Beutl.Media.Music;

namespace Beutl.Media.Source;

[JsonConverter(typeof(SoundSourceJsonConverter))]
public interface ISoundSource : IMediaSource
{
    TimeSpan Duration { get; }

    int SampleRate { get; }

    int NumChannels { get; }

    bool Read(int start, int length, [NotNullWhen(true)] out IPcm? sound);

    bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound);

    bool Read(TimeSpan start, int length, [NotNullWhen(true)] out IPcm? sound);

    bool Read(int start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound);

    new ISoundSource Clone();

    IMediaSource IMediaSource.Clone() => Clone();
}
