using Beutl.Audio.Effects;

namespace Beutl.Audio.Graph.Nodes;

public sealed class EffectNode : AudioNode
{
    private IAudioEffect? _effect;
    private IAudioEffectProcessor? _processor;
    private bool _needsReset = true;

    public IAudioEffect? Effect
    {
        get => _effect;
        set
        {
            if (_effect != value)
            {
                _processor?.Dispose();
                _processor = null;
                _effect = value;
                _needsReset = true;
            }
        }
    }

    public override AudioBuffer Process(AudioProcessContext context)
    {
        if (Inputs.Count != 1)
            throw new InvalidOperationException("Effect node requires exactly one input.");

        if (_effect == null || !_effect.IsEnabled)
        {
            // Pass through if no effect or disabled
            return Inputs[0].Process(context);
        }

        var input = Inputs[0].Process(context);

        // Ensure processor is created
        if (_processor == null)
        {
            _processor = _effect.CreateProcessor();
            _needsReset = true;
        }

        // Check if we need to prepare the processor
        if (_needsReset)
        {
            _processor.Prepare(context.TimeRange, context.SampleRate);
            _needsReset = false;
        }

        // Create output buffer
        var output = new AudioBuffer(input.SampleRate, input.ChannelCount, input.SampleCount);

        try
        {
            // Process with the new graph-based effect processor
            _processor.Process(input, output, context);
            return output;
        }
        catch (Exception ex)
        {
            output.Dispose();
            throw;
        }
    }

    public void ResetProcessor()
    {
        _processor?.Reset();
        _needsReset = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _processor?.Dispose();
            _processor = null;
            _effect = null;
        }

        base.Dispose(disposing);
    }
}
