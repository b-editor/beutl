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
    [DataContract]
    [CustomClipUI(Color = 0x6200ea)]
    public class Text : ImageObject
    {
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 100, float.NaN, 0);
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);
        public static readonly FontPropertyMetadata FontMetadata = new();
        public static readonly DocumentPropertyMetadata DocumentMetadata = new("");

        public Text()
        {
            Size = new(SizeMetadata);
            Color = new(ColorMetadata);
            Font = new(FontMetadata);
            Document = new(DocumentMetadata);
        }

        public override string Name => Resources.Text;
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
        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }
        [DataMember(Order = 1)]
        public ColorProperty Color { get; private set; }
        [DataMember(Order = 2)]
        public DocumentProperty Document { get; private set; }
        [DataMember(Order = 3)]
        public FontProperty Font { get; private set; }

        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            return Image.Text(Document.Text, Font.Select, Size[args.Frame], Color.Color);
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            Size.Load(SizeMetadata);
            Color.Load(ColorMetadata);
            Font.Load(FontMetadata);
            Document.Load(DocumentMetadata);
        }
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
