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

using NAudio.Wave;
using NAudio.CoreAudioApi;

using OpenTK.Audio.OpenAL;
using System.IO;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;
using BEditor.Core.Properties;
using BEditor.Core.Data.Primitive.Objects.PrimitiveImages;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    [CustomClipUI(Color = 0xff1744)]
    public class AudioObject : ObjectElement
    {
        public static readonly FilePropertyMetadata FileMetadata = Video.FileMetadata with { Filter = "mp3,wav,mp4", FilterName = "" };
        public static readonly EasePropertyMetadata VolumeMetadata = new("Volume", 50, 100, 0);
        private WaveOut player;
        private AudioFileReader reader;

        public AudioObject()
        {
            Volume = new(VolumeMetadata);
            File = new(FileMetadata);
        }

        public override string Name => "Audio";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Volume,
            File
        };
        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Volume { get; private set; }
        private WaveOut Player => player ??= new();
        private AudioFileReader Reader
        {
            get
            {
                if (reader is null && System.IO.File.Exists(File.File))
                {
                    reader = new(File.File);
                }

                return reader;
            }
            set
            {
                reader?.Dispose();
                reader = value;
            }
        }


        public unsafe override void Render(EffectRenderArgs args)
        {
            //if (args.Type is not RenderType.VideoPreview) return;

            if (reader is null) return;

            Player.Volume = Volume.GetValue(args.Frame) / 100;

            if (args.Frame == Parent.Start)
            {
                Player.Init(Reader);

                Player.Play();

                //using var audioreader = new AudioFileReader(File.File);
                //using var pcm = WaveFormatConversionStream.CreatePcmStream(audioreader);
            }
        }
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            Volume.ExecuteLoaded(VolumeMetadata);
            File.ExecuteLoaded(FileMetadata);

            File.Subscribe(file =>
            {
                if (System.IO.File.Exists(file))
                {
                    Reader = new(file);
                }
            });
        }
    }
}
