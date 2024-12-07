using Beutl.Animation;
using Beutl.Graphics.Rendering.V2;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Graphics;

public enum VideoPositionMode
{
    Manual,
    Automatic
}

public class SourceVideo : Drawable
{
    public static readonly CoreProperty<TimeSpan> OffsetPositionProperty;
    public static readonly CoreProperty<TimeSpan> PlaybackPositionProperty;
    public static readonly CoreProperty<VideoPositionMode> PositionModeProperty;
    public static readonly CoreProperty<IVideoSource?> SourceProperty;
    private TimeSpan _offsetPosition;
    private TimeSpan _playbackPosition;
    private VideoPositionMode _positionMode;
    private IVideoSource? _source;
    private TimeSpan _requestedPosition;

    static SourceVideo()
    {
        OffsetPositionProperty = ConfigureProperty<TimeSpan, SourceVideo>(nameof(OffsetPosition))
            .Accessor(o => o.OffsetPosition, (o, v) => o.OffsetPosition = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();

        PlaybackPositionProperty = ConfigureProperty<TimeSpan, SourceVideo>(nameof(PlaybackPosition))
            .Accessor(o => o.PlaybackPosition, (o, v) => o.PlaybackPosition = v)
            .DefaultValue(TimeSpan.Zero)
            .Register();

        PositionModeProperty = ConfigureProperty<VideoPositionMode, SourceVideo>(nameof(PositionMode))
            .Accessor(o => o.PositionMode, (o, v) => o.PositionMode = v)
            .DefaultValue(VideoPositionMode.Automatic)
            .Register();

        SourceProperty = ConfigureProperty<IVideoSource?, SourceVideo>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .Register();

        AffectsRender<SourceVideo>(
            OffsetPositionProperty,
            PlaybackPositionProperty,
            PositionModeProperty,
            SourceProperty);
    }

    public TimeSpan OffsetPosition
    {
        get => _offsetPosition;
        set => SetAndRaise(OffsetPositionProperty, ref _offsetPosition, value);
    }

    public TimeSpan PlaybackPosition
    {
        get => _playbackPosition;
        set => SetAndRaise(PlaybackPositionProperty, ref _playbackPosition, value);
    }

    public VideoPositionMode PositionMode
    {
        get => _positionMode;
        set => SetAndRaise(PositionModeProperty, ref _positionMode, value);
    }

    public IVideoSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        if (PositionMode == VideoPositionMode.Automatic)
        {
            if (_requestedPosition != clock.CurrentTime)
            {
                _requestedPosition = clock.CurrentTime;
                Invalidate();
            }
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (_source?.IsDisposed == false)
        {
            return _source.FrameSize.ToSize(1);
        }
        else
        {
            return Size.Empty;
        }
    }

    protected override void OnDraw(GraphicsContext2D context)
    {
        if (_source?.IsDisposed == false)
        {
            if (PositionMode == VideoPositionMode.Manual)
            {
                _requestedPosition = _playbackPosition;
            }

            TimeSpan pos = _requestedPosition + _offsetPosition;
            Rational rate = _source.FrameRate;
            double frameNum = pos.TotalSeconds * (rate.Numerator / (double)rate.Denominator);

            context.DrawVideoSource(_source, (int)frameNum, Brushes.White, null);
        }
    }
}
