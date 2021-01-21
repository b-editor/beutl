using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    [CustomClipUI(Color = 0x0091ea)]
    public class ImageFile : ImageObject
    {
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", "png,jpeg,jpg,bmp", Resources.ImageFile);
        private Image<BGRA32> source;
        private IDisposable disposable;

        public ImageFile()
        {
            File = new(FileMetadata);
        }

        public override string Name => Resources.Image;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            File
        };
        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }
        public Image<BGRA32> Source
        {
            get
            {
                if (source == null && System.IO.File.Exists(File.File))
                {
                    using var stream = new FileStream(File.File, FileMode.Open);
                    source = Image.Decode(stream);
                }

                return source;
            }
            set
            {
                source?.Dispose();
                source = value;
            }
        }

        public override Image<BGRA32> OnRender(EffectRenderArgs args) => Source?.Clone();
        protected override void OnLoad()
        {
            base.OnLoad();
            File.Load(FileMetadata);

            disposable = File.Subscribe(file =>
            {
                if (System.IO.File.Exists(file))
                {
                    using var stream = new FileStream(file, FileMode.Open);
                    Source = Image.Decode(stream);
                }
            });
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            File.Unload();

            disposable?.Dispose();
            Source = null;
        }
    }
}
