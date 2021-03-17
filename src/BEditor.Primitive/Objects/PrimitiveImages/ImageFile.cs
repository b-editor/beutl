using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Runtime.Serialization;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Properties;

using Reactive.Bindings;

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
        private ReactiveProperty<Image<BGRA32>?>? Source { get; set; }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            return Source?.Value?.Clone();
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            File.Load(FileMetadata);

            Source = File.Where(file => System.IO.File.Exists(file))
                .Select(f =>
                {
                    Source?.Value?.Dispose();

                    using var stream = new FileStream(f, FileMode.Open);
                    return Image.Decode(stream);
                })
                .ToReactiveProperty();

        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            File.Unload();

            Source?.Value?.Dispose();
            Source?.Dispose();
        }
    }
}
