using System.Diagnostics;

using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Source;

namespace Beutl.Audio;

public sealed class SourceSound : Sound
{
    public static readonly CoreProperty<ISoundSource?> SourceProperty;
    public static readonly CoreProperty<TimeSpan> OffsetPositionProperty;
    private ISoundSource? _source;
    private TimeSpan _offsetPosition;

    static SourceSound()
    {
        SourceProperty = ConfigureProperty<ISoundSource?, SourceSound>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .DefaultValue(null)
            .Register();

        OffsetPositionProperty = ConfigureProperty<TimeSpan, SourceSound>(nameof(OffsetPosition))
            .Accessor(o => o.OffsetPosition, (o, v) => o.OffsetPosition = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();

        AffectsRender<SourceSound>(SourceProperty, OffsetPositionProperty);
    }

    public ISoundSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    public TimeSpan OffsetPosition
    {
        get => _offsetPosition;
        set => SetAndRaise(OffsetPositionProperty, ref _offsetPosition, value);
    }

    protected override void OnRecord(IAudio audio, TimeRange range)
    {
        if (Source?.IsDisposed != false)
            return;

        TimeSpan start = range.Start + OffsetPosition;
        if (start >= TimeSpan.Zero)
        {
            if (Source.Read(start, range.Duration, out IPcm? pcm))
            {
                audio.RecordPcm(pcm);
                pcm.Dispose();
            }
        }
        else
        {
            TimeRange range2 = range.WithStart(start);
            if (range2.End <= TimeSpan.Zero)
            {
                return;
            }
            else
            {
                if (Source.Read(0, range2.End, out IPcm? pcm))
                {
                    TimeSpan offset = -start;
                    if (offset < TimeSpan.Zero || offset > TimeSpan.FromSeconds(1))
                    {
                        audio.RecordPcm(pcm);
                        pcm.Dispose();
                    }
                    else
                    {
                        using (audio.PushOffset(offset))
                        {
                            audio.RecordPcm(pcm);
                            pcm.Dispose();
                        }
                    }
                }
            }
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
