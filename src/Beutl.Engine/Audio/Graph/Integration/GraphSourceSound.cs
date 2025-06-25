using System;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Audio.Graph.Integration;

public sealed class GraphSourceSound : GraphSound
{
    public static readonly CoreProperty<ISoundSource?> SourceProperty;
    public static readonly CoreProperty<TimeSpan> OffsetPositionProperty;

    private ISoundSource? _source;
    private TimeSpan _offsetPosition;

    static GraphSourceSound()
    {
        SourceProperty = ConfigureProperty<ISoundSource?, GraphSourceSound>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .DefaultValue(null)
            .Register();

        OffsetPositionProperty = ConfigureProperty<TimeSpan, GraphSourceSound>(nameof(OffsetPosition))
            .Accessor(o => o.OffsetPosition, (o, v) => o.OffsetPosition = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();
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

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        hash.Add(_source?.GetHashCode() ?? 0);
        hash.Add(_offsetPosition);
        return hash.ToHashCode();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source?.Dispose();
            _source = null;
        }
        
        base.Dispose(disposing);
    }
}