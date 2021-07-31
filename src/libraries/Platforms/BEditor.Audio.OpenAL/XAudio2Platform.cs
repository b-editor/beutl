using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Audio.Platform;

namespace BEditor.Audio.XAudio2
{
    public class XAudio2Platform : IAudioPlatform
    {
        public IAudioContextImpl CreateContext()
        {
            return new AudioContextImpl();
        }
    }
}
