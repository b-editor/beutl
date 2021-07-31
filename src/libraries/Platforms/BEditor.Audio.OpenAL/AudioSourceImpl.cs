using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Audio.Platform;

using Vortice.Multimedia;

namespace BEditor.Audio.XAudio2
{
    public sealed class AudioSourceImpl : IAudioSourceImpl
    {
        private readonly AudioContextImpl _context;
        private float _volume = 1.0f;

        internal Vortice.XAudio2.IXAudio2SourceVoice? SourceVoice { get; private set; }

        public AudioSourceImpl(AudioContextImpl context)
        {
            _context = context;
        }

        private void SetupVoice(AudioFormat format)
        {
            var wFmt = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
            SourceVoice = _context.Device.CreateSourceVoice(wFmt);

            //if (_submixer != null)
            //{
            //    var vsDesc = new VoiceSendDescriptor(_submixer.SubMixerVoice);
            //    SourceVoice.SetOutputVoices(new VoiceSendDescriptor[] { vsDesc });
            //}
        }

        public int BuffersQueued => SourceVoice?.State.BuffersQueued ?? -1;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                SourceVoice?.SetVolume(_volume);
            }
        }

        public bool Looping { get; set; }

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

        public void QueueBuffer(AudioBuffer buffer)
        {
            var impl = buffer.PlatformImpl;
            if (SourceVoice == null)
            {
                SetupVoice(buffer.Format);
            }

            var xaBuffer = (AudioBufferImpl)impl;

            if(xaBuffer.Buffer != null)
            {
                if (Looping)
                {
                    xaBuffer.Buffer.LoopCount = 255;
                }

                SourceVoice?.SubmitSourceBuffer(xaBuffer.Buffer);
            }
        }

        public void Flush()
        {
            SourceVoice?.FlushSourceBuffers();
        }
    }
}
