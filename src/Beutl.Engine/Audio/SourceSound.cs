using System;
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

    protected override ISoundSource? GetSoundSource()
    {
        return _source;
    }

    protected override void OnRecord(IAudio audio, TimeRange range)
    {
        // This method is kept for compatibility but the new graph system handles everything
        // through GetSoundSource() and the graph processing
        if (Source?.IsDisposed != false)
            return;

        // The graph system will handle offset position automatically
        // This is only called for legacy compatibility
        TimeSpan start = range.Start + OffsetPosition;
        if (start >= TimeSpan.Zero)
        {
            if (Source.Read(start, range.Duration, out IPcm? pcm))
            {
                audio.Write(pcm);
                pcm.Dispose();
            }
        }
        else
        {
            TimeRange range2 = range.WithStart(start);
            if (range2.End > TimeSpan.Zero)
            {
                if (Source.Read(TimeSpan.Zero, range2.End, out IPcm? pcm))
                {
                    audio.Write(pcm);
                    pcm.Dispose();
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

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(_source?.GetHashCode() ?? 0);
        hash.Add(_offsetPosition);
        return hash.ToHashCode();
    }

    internal void DisposeSource()
    {
        _source?.Dispose();
        _source = null;
    }
}
