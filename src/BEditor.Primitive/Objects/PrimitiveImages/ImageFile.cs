using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that references an image file.
    /// </summary>
    [DataContract]
    [CustomClipUI(Color = 0x0091ea)]
    public class ImageFile : ImageObject
    {
        /// <summary>
        /// Represents <see cref="File"/> metadata.
        /// </summary>
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", new(Resources.ImageFile, new FileExtension[]
        {
            new("png"),
            new("jpeg"),
            new("jpg"),
            new("bmp"),
        }));
        private Image<BGRA32>? _Source;
        private IDisposable? _Disposable;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageFile"/> class.
        /// </summary>
        public ImageFile()
        {
            File = new(FileMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Image;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            File
        };
        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the image file to reference.
        /// </summary>
        [DataMember(Order = 0)]
        public FileProperty File { get; private set; }
        private Image<BGRA32>? Source
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

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            return Source?.Clone();
        }

        /// <inheritdoc/>
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
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            File.Unload();

            _Disposable?.Dispose();
            Source = null;
        }
    }
}
