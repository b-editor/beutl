using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Source;
using Beutl.Rendering;

namespace Beutl.Audio;

public sealed class SourcedSound : Sound
{
    public static readonly CoreProperty<ISoundSource?> SourceProperty;
    private ISoundSource? _source;

    static SourcedSound()
    {
        SourceProperty = ConfigureProperty<ISoundSource?, SourcedSound>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .DefaultValue(null)
            .SerializeName("source")
            .Register();

        AffectsRender<SourcedSound>(SourceProperty);
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

public abstract class Sound : Renderable
{
    public static readonly CoreProperty<float> GainProperty;
    private float _gain = 1;
    private LayerNode? _layerNode;
    private TimeRange _range;
    private TimeSpan _offset;
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);

    static Sound()
    {
        GainProperty = ConfigureProperty<float, Sound>(nameof(Gain))
            .Accessor(o => o.Gain, (o, v) => o.Gain = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .DefaultValue(1)
            .SerializeName("gain")
            .Register();

        AffectsRender<Sound>(GainProperty);
    }

    public Sound()
    {
    }

    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, value);
    }

    public TimeSpan Duration { get; private set; }

    public override void Render(IRenderer renderer)
    {
        UpdateTime(renderer.Clock);
        Record(renderer.Audio);
    }

    public void Record(IAudio audio)
    {
        using (audio.PushGain(_gain))
        using (audio.PushOffset(_offset))
        {
            OnRecord(audio, _range);
        }
    }

    protected abstract void OnRecord(IAudio audio, TimeRange range);

    public void Time(TimeSpan available)
    {
        Duration = TimeCore(available);
    }

    protected abstract TimeSpan TimeCore(TimeSpan available);

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        _layerNode = args.Parent as LayerNode;
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        _layerNode = null;
    }

    private void UpdateTime(IClock clock)
    {
        if (_layerNode != null)
        {
            double current = Math.Floor(clock.CurrentTime.TotalSeconds);
            var currentTime = TimeSpan.FromSeconds(current);

            TimeSpan start = currentTime - _layerNode.Start;
            TimeSpan length;

            if (start < TimeSpan.Zero)
            {
                _offset = start.Negate();
                length = s_second + start;
                start = TimeSpan.Zero;
            }
            else
            {
                _offset = TimeSpan.Zero;
                length = _layerNode.Range.End - start;
                if (length > s_second)
                {
                    length = s_second;
                }
            }

            _range = new TimeRange(start, length);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        UpdateTime(clock);
    }
}
