using System;

using Vortice.XAudio2;

namespace BEditor.Audio.XAudio2
{
    public sealed class XAudioSource : IDisposable
    {
        private readonly XAudioContext _context;
        private IXAudio2SourceVoice? SourceVoice;

        public XAudioSource(XAudioContext context)
        {
            _context = context;
        }

        public int BuffersQueued => SourceVoice?.State.BuffersQueued ?? -1;

        public void Dispose()
        {
            SourceVoice?.DestroyVoice();
            SourceVoice?.Dispose();
        }

        public bool IsPlaying()
        {
            return SourceVoice?.State.BuffersQueued > 0;
        }

        public void Play()
        {
            SourceVoice?.Start();
        }

        public void Stop()
        {
            SourceVoice?.Stop();
        }

        public void QueueBuffer(XAudioBuffer buffer)
        {
            if (SourceVoice == null)
            {
                SourceVoice = _context.Device.CreateSourceVoice(buffer.Format!);
            }

            SourceVoice.SubmitSourceBuffer(buffer.Buffer);
        }

        public void Flush()
        {
            SourceVoice?.FlushSourceBuffers();
        }
    }
}