using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.Audio.Effects;

internal sealed class AudioEffectProcessorGroup : IAudioEffectProcessor
{
    public IAudioEffectProcessor[] Processors { get; set; } = [];

    public void Prepare(TimeRange range, int sampleRate)
    {
        foreach (IAudioEffectProcessor processor in Processors)
        {
            processor.Prepare(range, sampleRate);
        }
    }

    public void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context)
    {
        if (Processors.Length == 0)
        {
            input.CopyTo(output);
            return;
        }

        AudioBuffer? current = input;
        foreach (IAudioEffectProcessor processor in Processors)
        {
            var next = new AudioBuffer(
                output.SampleRate,
                output.ChannelCount,
                output.SampleCount);
            processor.Process(current, next, context);
            current = next;
        }

        current.CopyTo(output);
    }

    public void Dispose()
    {
        foreach (IAudioEffectProcessor processor in Processors)
        {
            if (processor is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public void Reset()
    {
        foreach (IAudioEffectProcessor processor in Processors)
        {
            processor.Reset();
        }
    }
}
