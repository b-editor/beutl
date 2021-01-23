using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    [CustomClipUI(Color = 0x0091ea)]
    public class ImageFile : ImageObject
    {
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", new(Resources.ImageFile, new FileExtension[]
        {
            new("png"),
            new("jpeg"),
            new("jpg"),
            new("bmp"),
        }));
        private Image<BGRA32>? _Source;
        private IDisposable? _Disposable;

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
        public Image<BGRA32>? Source
        {
            get
            {
                if (_Source == null && System.IO.File.Exists(File.File))
                {
                    using var stream = new FileStream(File.File, FileMode.Open);
                    _Source = Image.Decode(stream);
                }

                return _Source;
            }
            set
            {
                _Source?.Dispose();
                _Source = value;
            }
        }

        protected override Image<BGRA32>? OnRender(EffectRenderArgs args) => Source?.Clone();
        protected override void OnLoad()
        {
            base.OnLoad();
            File.Load(FileMetadata);

            _Disposable = File.Subscribe(file =>
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

            _Disposable?.Dispose();
            Source = null;
        }
    }
}
