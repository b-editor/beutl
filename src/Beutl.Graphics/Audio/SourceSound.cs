using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Source;

namespace Beutl.Audio;

public sealed class SourceSound : Sound
{
    public static readonly CoreProperty<ISoundSource?> SourceProperty;
    private ISoundSource? _source;

    static SourceSound()
    {
        SourceProperty = ConfigureProperty<ISoundSource?, SourceSound>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .DefaultValue(null)
            .Register();

        AffectsRender<SourceSound>(SourceProperty);
    }

    public ISoundSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    protected override void OnRecord(IAudio audio, TimeRange range)
    {
        if (Source?.IsDisposed == false
            && Source.Read(range.Start, range.Duration, out IPcm? pcm))
        {
            audio.RecordPcm(pcm);
            pcm.Dispose();
        }
    }

    protected override TimeSpan TimeCore(TimeSpan available)
    {
        if (Source != null)
        {
            return Source.Duration;
        }
        else
        {
            return available;
        }
    }
}
