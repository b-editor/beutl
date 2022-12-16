using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Effects;

public sealed class Delay : SoundEffect
{
    public static readonly CoreProperty<float> DelayTimeProperty;
    private const float MaxDelayTime = 5;
    private float _delayTime;

    static Delay()
    {
        DelayTimeProperty = ConfigureProperty<float, Delay>(o => o.DelayTime)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .SerializeName("delay-time")
            .Maximum(MaxDelayTime)
            .Register();

        AffectsRender<Delay>(DelayTimeProperty);
    }

    public float DelayTime
    {
        get => _delayTime;
        set => SetAndRaise(DelayTimeProperty, ref _delayTime, value);
    }

    public override ISoundProcessor CreateProcessor()
    {
        return new DelayProcessor(_delayTime);
    }

    private sealed class DelayProcessor : ISoundProcessor
    {
        private readonly float _delayTime;
        private Pcm<Stereo32BitFloat>? _delayBuffer;
        private int _delayWritePosition;

        public DelayProcessor(float delayTime)
        {
            _delayTime = delayTime;
        }

        public void Dispose()
        {
            _delayBuffer?.Dispose();
            _delayBuffer = null;
        }

        [MemberNotNull(nameof(_delayBuffer))]
        private void Initialize(Pcm<Stereo32BitFloat> pcm)
        {
            if (_delayBuffer == null)
            {
                int bufferSamples = (int)(MaxDelayTime * pcm.SampleRate) + 1;

                if (bufferSamples < 1)
                    bufferSamples = 1;

                _delayBuffer = new Pcm<Stereo32BitFloat>(pcm.SampleRate, bufferSamples);
                _delayWritePosition = 0;
            }
        }

        public void Process(in Pcm<Stereo32BitFloat> src, out Pcm<Stereo32BitFloat> dst)
        {
            float delay_time_value = _delayTime * src.SampleRate;
            int local_write_position;

            Initialize(src);
            var delay_buffer_samples = _delayBuffer.NumSamples;
            Span<Stereo32BitFloat> channel_data = src.DataSpan;
            Span<Vector2> delay_data = MemoryMarshal.Cast<Stereo32BitFloat, Vector2>(_delayBuffer.DataSpan);
            local_write_position = _delayWritePosition;

            for (int sample = 0; sample < channel_data.Length; sample++)
            {
                ref Vector2 @in = ref Unsafe.As<Stereo32BitFloat, Vector2>(ref channel_data[sample]);

                float read_position = local_write_position - delay_time_value + (float)delay_buffer_samples % delay_buffer_samples;
                int local_read_position = (int)MathF.Floor(read_position);

                if (local_read_position != local_write_position)
                {
                    float fraction = read_position - local_read_position;
                    Vector2 delayed1 = delay_data[(local_read_position + 0)];
                    Vector2 delayed2 = delay_data[(local_read_position + 1) % delay_buffer_samples];
                    Vector2 @out = delayed1 + fraction * (delayed2 - delayed1);

                    @in = @in + (@out - @in);
                    delay_data[local_write_position] = @in;
                }

                if (++local_write_position >= delay_buffer_samples)
                    local_write_position -= delay_buffer_samples;
            }

            _delayWritePosition = local_write_position;

            dst = src;
        }
    }
}
