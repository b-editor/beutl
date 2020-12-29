using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Control;
using BEditor.Core.Data.Property;
using BEditor.Media;
using BEditor.Media.PCM;

using OpenTK.Audio.OpenAL;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    [CustomClipUI(Color = 0xff1744)]
    public class AudioObject : ObjectElement
    {
        public override string Name => "Audio SinWave";
        public override IEnumerable<PropertyElement> Properties => Array.Empty<PropertyElement>();

        public unsafe override void Render(EffectRenderArgs args)
        {
            if (args.Frame != Parent.Start) return;

            var source = AL.GenSource();
            var buffer = AL.GenBuffer();

            var sound = Sound.SinWave(44100);

            fixed (PCM16* pcm = sound.Pcm)
            {
                AL.BufferData(buffer, ALFormat.Mono16, (IntPtr)pcm, (int)sound.DataSize, 44100);
            }
            AL.Source(source, ALSourcei.Buffer, buffer);

            AL.SourcePlay(source);

            AL.DeleteBuffer(buffer);
            AL.DeleteSource(source);
        }
    }
}
