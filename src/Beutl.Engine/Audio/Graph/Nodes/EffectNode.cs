using System;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph.Exceptions;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Graph.Nodes;

public sealed class EffectNode : AudioNode
{
    private ISoundEffect? _effect;
    private ISoundProcessor? _processor;
    private bool _needsReset = true;

    public ISoundEffect? Effect
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

        // Convert AudioBuffer to Pcm<Stereo32BitFloat> for existing processor
        using var inputPcm = ConvertToPcm(input);
        
        try
        {
            // Process with existing ISoundProcessor interface
            _processor.Process(in inputPcm, out var outputPcm);
            
            using (outputPcm)
            {
                // Convert back to AudioBuffer
                return ConvertFromPcm(outputPcm, context.SampleRate);
            }
        }
        catch (Exception ex)
        {
            throw new AudioEffectException($"Error processing effect: {_effect.GetType().Name}", _effect.GetType().Name, this, ex);
        }
    }

    private unsafe Pcm<Stereo32BitFloat> ConvertToPcm(AudioBuffer buffer)
    {
        if (buffer.ChannelCount != 2)
            throw new NotSupportedException("Effect processing currently only supports stereo audio.");

        var pcm = new Pcm<Stereo32BitFloat>(buffer.SampleRate, buffer.SampleCount);
        var pcmPtr = (Stereo32BitFloat*)pcm.Data;
        
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);
        
        for (int i = 0; i < buffer.SampleCount; i++)
        {
            pcmPtr[i] = new Stereo32BitFloat(leftChannel[i], rightChannel[i]);
        }
        
        return pcm;
    }

    private unsafe AudioBuffer ConvertFromPcm(Pcm<Stereo32BitFloat> pcm, int sampleRate)
    {
        var buffer = new AudioBuffer(sampleRate, 2, pcm.NumSamples);
        var pcmPtr = (Stereo32BitFloat*)pcm.Data;
        
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);
        
        for (int i = 0; i < pcm.NumSamples; i++)
        {
            leftChannel[i] = pcmPtr[i].Left;
            rightChannel[i] = pcmPtr[i].Right;
        }
        
        return buffer;
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