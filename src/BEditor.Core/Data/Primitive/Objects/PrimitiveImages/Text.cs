using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> to draw a string.
    /// </summary>
    [DataContract]
    [CustomClipUI(Color = 0x6200ea)]
    public class Text : ImageObject
    {
        /// <summary>
        /// Represents <see cref="Size"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 100, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);
        /// <summary>
        /// Represents <see cref="Font"/> metadata.
        /// </summary>
        public static readonly FontPropertyMetadata FontMetadata = new();
        /// <summary>
        /// Represents <see cref="Document"/> metadata.
        /// </summary>
        public static readonly DocumentPropertyMetadata DocumentMetadata = new("");

        /// <summary>
        /// Initializes a new instance of the <see cref="Text"/> class.
        /// </summary>
        public Text()
        {
            Size = new(SizeMetadata);
            Color = new(ColorMetadata);
            Font = new(FontMetadata);
            Document = new(DocumentMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Text;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Size,
            Color,
            Document,
            Font
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the size of the string to be drawn.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color of the string to be drawn.
        /// </summary>
        [DataMember(Order = 1)]
        public ColorProperty Color { get; private set; }
        /// <summary>
        /// Get the <see cref="DocumentProperty"/> representing the string to be drawn.
        /// </summary>
        [DataMember(Order = 2)]
        public DocumentProperty Document { get; private set; }
        /// <summary>
        /// Get the <see cref="FontProperty"/> that represents the font of the string to be drawn.
        /// </summary>
        [DataMember(Order = 3)]
        public FontProperty Font { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            return Image.Text(Document.Text, Font.Select, Size[args.Frame], Color.Color);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            Size.Load(SizeMetadata);
            Color.Load(ColorMetadata);
            Font.Load(FontMetadata);
            Document.Load(DocumentMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            Size.Unload();
            Color.Unload();
            Font.Unload();
            Document.Unload();
        }
    }
}
