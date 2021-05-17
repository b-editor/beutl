using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media.Encoding;
using BEditor.Media.PCM;

namespace BEditor.Media.FFmpeg.Encoding
{
    public class AudioOutputStream : IAudioOutputStream
    {
        private readonly FFMediaToolkit.Encoding.AudioOutputStream _stream;

        public AudioOutputStream(FFMediaToolkit.Encoding.AudioOutputStream stream, AudioEncoderSettings config)
        {
            _stream = stream;
            Configuration = config;
        }

        public AudioEncoderSettings Configuration { get; }

        public TimeSpan CurrentDuration => _stream.CurrentDuration;

        public void AddFrame(Sound<StereoPCMFloat> sound)
        {
            _stream.AddFrame(sound.Extract());
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
