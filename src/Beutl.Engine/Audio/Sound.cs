using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Rendering;

namespace Beutl.Audio;

// Animationに対応させる (手動)

public abstract class Sound : Renderable
{
    public static readonly CoreProperty<float> GainProperty;
    public static readonly CoreProperty<ISoundEffect?> EffectProperty;
    private float _gain = 100;
    private TimeRange _range;
    private TimeSpan _offset;
    private ISoundEffect? _effect;
    private ISoundProcessor? _effectProcessor;
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(1);

    static Sound()
    {
        GainProperty = ConfigureProperty<float, Sound>(nameof(Gain))
            .Accessor(o => o.Gain, (o, v) => o.Gain = v)
            .DefaultValue(100)
            .Register();

        EffectProperty = ConfigureProperty<ISoundEffect?, Sound>(nameof(Effect))
            .Accessor(o => o.Effect, (o, v) => o.Effect = v)
            .DefaultValue(null)
            .Register();

        AffectsRender<Sound>(GainProperty, EffectProperty);
    }

    public Sound()
    {
        Invalidated += OnInvalidated;
    }

    private void OnInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        if (e.PropertyName is nameof(Effect))
        {
            InvalidateEffectProcessor();
        }
    }

    private void InvalidateEffectProcessor()
    {
        _effectProcessor?.Dispose();
        _effectProcessor = null;
    }

    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, value);
    }

    public TimeSpan Duration { get; private set; }

    public ISoundEffect? Effect
    {
        get => _effect;
        set => SetAndRaise(EffectProperty, ref _effect, value);
    }

    public Pcm<Stereo32BitFloat> ToPcm(int sampleRate)
    {
        using (var audio = new Audio(sampleRate))
        using (audio.PushGain(_gain / 100f))
        using (audio.PushOffset(_offset))
        {
            OnRecord(audio, _range);

            return audio.GetPcm();
        }
    }

    public void Render(IAudio audio)
    {
        if (_effect is { IsEnabled: true } effect)
        {
            _effectProcessor ??= effect.CreateProcessor();

            Pcm<Stereo32BitFloat> pcm = ToPcm(audio.SampleRate);
            _effectProcessor.Process(in pcm, out Pcm<Stereo32BitFloat>? outPcm);
            if (pcm != outPcm)
            {
                pcm.Dispose();
            }

            audio.RecordPcm(outPcm);

            outPcm.Dispose();
        }
        else
        {
            using (audio.PushGain(_gain / 100f))
            using (audio.PushOffset(_offset))
            {
                OnRecord(audio, _range);
            }
        }
    }

    protected abstract void OnRecord(IAudio audio, TimeRange range);

    public void Time(TimeSpan available)
    {
        Duration = TimeCore(available);
    }

    protected abstract TimeSpan TimeCore(TimeSpan available);

    private void UpdateTime(IClock clock)
    {
        TimeSpan start = clock.AudioStartTime;
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
            length = clock.DurationTime - start;
            if (length > s_second)
            {
                length = s_second;
            }
        }

        if (_range.Start > start)
        {
            InvalidateEffectProcessor();
        }

        _range = new TimeRange(start, length);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        UpdateTime(clock);
    }
}
