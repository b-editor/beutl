using System;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Effects;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Effects;

/// <summary>
/// Wraps old ISoundEffect to work with new IAudioEffect interface
/// </summary>
public sealed class SoundEffectWrapper : Animatable, IAudioEffect
{
    private readonly ISoundEffect _oldEffect;
    
    public SoundEffectWrapper(ISoundEffect oldEffect)
    {
        _oldEffect = oldEffect ?? throw new ArgumentNullException(nameof(oldEffect));
    }
    
    public ISoundEffect InnerEffect => _oldEffect;
    
    public bool IsEnabled => _oldEffect.IsEnabled;
    
    public IAudioEffectProcessor CreateProcessor()
    {
        return new ProcessorWrapper(_oldEffect.CreateProcessor());
    }
    
    private sealed class ProcessorWrapper : IAudioEffectProcessor
    {
        private readonly ISoundProcessor _oldProcessor;
        private Pcm<Stereo32BitFloat>? _tempInput;
        private Pcm<Stereo32BitFloat>? _tempOutput;
        
        public ProcessorWrapper(ISoundProcessor oldProcessor)
        {
            _oldProcessor = oldProcessor ?? throw new ArgumentNullException(nameof(oldProcessor));
        }
        
        public void Prepare(TimeRange range, int sampleRate)
        {
            // Old API doesn't have prepare method
        }
        
        public void Process(AudioBuffer input, AudioBuffer output, AudioProcessContext context)
        {
            // Ensure buffers have same dimensions
            if (input.SampleRate != output.SampleRate || 
                input.ChannelCount != output.ChannelCount ||
                input.SampleCount != output.SampleCount)
            {
                throw new ArgumentException("Input and output buffers must have same dimensions");
            }
            
            // Create temporary Pcm buffers if needed
            if (_tempInput == null || _tempInput.NumSamples != input.SampleCount)
            {
                _tempInput?.Dispose();
                _tempInput = new Pcm<Stereo32BitFloat>(input.SampleRate, input.SampleCount);
            }
            
            // Convert AudioBuffer to Pcm<Stereo32BitFloat>
            ConvertToPcm(input, _tempInput);
            
            // Process using old API
            _oldProcessor.Process(in _tempInput, out var processedPcm);
            _tempOutput = processedPcm;
            
            // Convert back to AudioBuffer
            if (_tempOutput != null)
            {
                ConvertFromPcm(_tempOutput, output);
            }
            else
            {
                // If no output, just copy input to output
                for (int ch = 0; ch < Math.Min(input.ChannelCount, output.ChannelCount); ch++)
                {
                    input.GetChannelData(ch).CopyTo(output.GetChannelData(ch));
                }
            }
        }
        
        public void Reset()
        {
            // Old API doesn't have reset method
            // Recreate processor to simulate reset
        }
        
        private unsafe void ConvertToPcm(AudioBuffer buffer, Pcm<Stereo32BitFloat> pcm)
        {
            var pcmData = pcm.DataSpan;
            
            if (buffer.ChannelCount == 1)
            {
                // Mono to stereo
                var monoData = buffer.GetChannelData(0);
                for (int i = 0; i < buffer.SampleCount; i++)
                {
                    pcmData[i] = new Stereo32BitFloat(monoData[i], monoData[i]);
                }
            }
            else if (buffer.ChannelCount >= 2)
            {
                // Stereo (use first two channels)
                var leftData = buffer.GetChannelData(0);
                var rightData = buffer.GetChannelData(1);
                for (int i = 0; i < buffer.SampleCount; i++)
                {
                    pcmData[i] = new Stereo32BitFloat(leftData[i], rightData[i]);
                }
            }
        }
        
        private unsafe void ConvertFromPcm(Pcm<Stereo32BitFloat> pcm, AudioBuffer buffer)
        {
            var pcmData = pcm.DataSpan;
            
            // Always convert to the number of channels in the output buffer
            for (int ch = 0; ch < buffer.ChannelCount; ch++)
            {
                var channelData = buffer.GetChannelData(ch);
                
                if (ch == 0) // Left channel
                {
                    for (int i = 0; i < buffer.SampleCount; i++)
                    {
                        channelData[i] = pcmData[i].Left;
                    }
                }
                else if (ch == 1) // Right channel
                {
                    for (int i = 0; i < buffer.SampleCount; i++)
                    {
                        channelData[i] = pcmData[i].Right;
                    }
                }
                else // Additional channels - duplicate stereo
                {
                    for (int i = 0; i < buffer.SampleCount; i++)
                    {
                        channelData[i] = (pcmData[i].Left + pcmData[i].Right) * 0.5f;
                    }
                }
            }
        }
        
        public void Dispose()
        {
            _tempInput?.Dispose();
            _tempOutput?.Dispose();
            _oldProcessor?.Dispose();
        }
    }
}