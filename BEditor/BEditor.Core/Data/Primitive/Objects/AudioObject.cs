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
        public static readonly FilePropertyMetadata FileMetadata = Video.FileMetadata with { Filter = "mp3,wav", FilterName = "" };
        public static readonly EasePropertyMetadata VolumeMetadata = new("Volume", 50, 100, 0);
        public static readonly ValuePropertyMetadata StartMetadata = new(Resources.Start + "(Milliseconds)", 0, Min: 0);
        private WaveOut player;
        private AudioFileReader reader;

        public AudioObject()
        {
            Volume = new(VolumeMetadata);
            File = new(FileMetadata);
            Start = new(StartMetadata);
        }

        public override string Name => "Audio";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Volume,
            Start,
            File
        };
        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Volume { get; private set; }
        [DataMember(Order = 2)]
        public ValueProperty Start { get; private set; }
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


        public override void Render(EffectRenderArgs args)
        {
            Player.Volume = Volume.GetValue(args.Frame) / 100;
            if (args.Type is not RenderType.VideoPreview) return;

            if (reader is null) return;


            if (args.Frame == Parent.Start)
            {
                Task.Run(async () =>
                {
                    Reader.CurrentTime = TimeSpan.FromMilliseconds(Start.Value);
                    Player.Init(Reader);

                    Player.Play();

                    var millis = (int)Parent.Length.ToMilliseconds(Parent.Parent.Parent.Framerate);
                    await Task.Delay(millis);

                    Player.Stop();
                });
            }
        }
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            Volume.ExecuteLoaded(VolumeMetadata);
            File.ExecuteLoaded(FileMetadata);
            Start.ExecuteLoaded(StartMetadata);

            File.Subscribe(file =>
            {
                if (System.IO.File.Exists(file))
                {
                    Reader = new(file);
                }
            });

            var player = Parent.Parent.Player;
            player.Stopped += (_, _) =>
            {
                Player.Stop();
            };

            player.Playing += async (_, e) =>
            {
                if (Parent.Start <= e.StartFrame && e.StartFrame <= Parent.End)
                {
                    var framerate = Parent.Parent.Parent.Framerate;
                    var startmsec = e.StartFrame.ToMilliseconds(framerate);

                    Reader.CurrentTime = TimeSpan.FromMilliseconds(Start.Value + startmsec);
                    Player.Init(Reader);

                    Player.Play();

                    // クリップ基準の再生開始位置
                    var hStart = startmsec - Parent.Start.ToMilliseconds(framerate);

                    var millis = (int)(Parent.Length.ToMilliseconds(framerate) - hStart);
                    await Task.Delay(millis);

                    Player.Stop();
                }
            };
        }
    }
}
