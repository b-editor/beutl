using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Media;
using BEditor.Media.Decoder;
using BEditor.Media.PCM;

using NAudio.Wave;

using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.Extensions.EXT.Float32;
using OpenTK.Audio.OpenAL.Extensions.EXT.FloatFormat;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that references an audio file.
    /// </summary>
    [DataContract]
    [CustomClipUI(Color = 0xff1744)]
    public class AudioObject : ObjectElement
    {
        /// <summary>
        /// Represens <see cref="File"/> metadata.
        /// </summary>
        public static readonly FilePropertyMetadata FileMetadata = VideoFile.FileMetadata with { Filter = new("", new FileExtension[] { new("mp3"), new("wav") }) };
        /// <summary>
        /// Represents <see cref="Volume"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata VolumeMetadata = new("Volume", 50, 100, 0);
        /// <summary>
        /// Represents <see cref="Start"/> metadata.
        /// </summary>
        public static readonly ValuePropertyMetadata StartMetadata = new(Resources.Start + "(Milliseconds)", 0, Min: 0);
        private FFmpegDecoder? _decoder;
        private IDisposable? _disposable;

        /// <summary>
        /// Initializes a new instance of <see cref="AudioObject"/> class.
        /// </summary>
        public AudioObject()
        {
            Volume = new(VolumeMetadata);
            File = new(FileMetadata);
            Start = new(StartMetadata);
        }

        /// <inheritdoc/>
        public override string Name => "Audio";
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Volume,
            Start,
            File
        };
        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the file to reference.
        /// </summary>
        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the volume.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty Volume { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [DataMember(Order = 2)]
        public ValueProperty Start { get; private set; }
        private FFmpegDecoder? Decoder
        {
            get
            {
                if (_decoder is null && System.IO.File.Exists(File.Value))
                {
                    _decoder = new(File.Value);
                }

                return _decoder;
            }
            set
            {
                _decoder?.Dispose();
                _decoder = value;
            }
        }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            if (Decoder is null /*|| args.Type is not RenderType.VideoPreview*/) return;

            var src = AL.GenSource();
            var buf = AL.GenBuffer();
            var time = (args.Frame - Parent!.Start).ToTimeSpan(Parent!.Parent.Parent!.Framerate);
            Decoder.Read(time, out Sound<PCM16> left, out _);

            AL.BufferData(buf, ALFormat.Mono16, left.Pcm, (int)left.Samplingrate);

            AL.Source(src, ALSourcei.Buffer, buf);
            AL.Source(src, ALSourcef.Gain, Volume[args.Frame] / 100f);

            AL.SourcePlay(src);

            Task.Run(async () =>
            {
                await Task.Delay(1000);

                AL.DeleteBuffer(buf);
                AL.DeleteSource(src);
            });
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Volume.Load(VolumeMetadata);
            File.Load(FileMetadata);
            Start.Load(StartMetadata);

            _disposable = File.Subscribe(file =>
            {
                if (System.IO.File.Exists(file))
                {
                    Decoder = new(file);
                }
            });
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _disposable?.Dispose();
        }
    }
}
