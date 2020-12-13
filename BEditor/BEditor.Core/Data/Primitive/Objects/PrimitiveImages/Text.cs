using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    [CustomClipUI(Color = 0x6200ea)]
    public class Text : ImageObject
    {
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 100, float.NaN, 0);
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, 255, 255, 255);
        public static readonly PropertyElementMetadata FontMetadata = new(Resources.DetailedSettings);
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

        public override Image<BGRA32> OnRender(EffectRenderArgs args) => Services.ImageRenderService.Text(
            (int)Size.GetValue(args.Frame),
            Color.Color,
            Document.Text,
            Font.Font.Select,
            (string)Font.Style.SelectItem,
            Font.RightToLeft.IsChecked);
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            Size.ExecuteLoaded(SizeMetadata);
            Color.ExecuteLoaded(ColorMetadata);
            Font.ExecuteLoaded(FontMetadata);
            Document.ExecuteLoaded(DocumentMetadata);
        }

        [DataContract(Namespace = "")]
        public class FontProperty : ExpandGroup
        {
            public static readonly FontPropertyMetadata FontMetadata = new();
            public static readonly SelectorPropertyMetadata FontStyleMetadata = new(Core.Properties.Resources.FontStyles,
                                                                                                      Primitive.Properties.FontProperty.FontStylesList);
            public static readonly CheckPropertyMetadata RightToLeftMetadata = new("RightToLeft", false);

            #region コンストラクタ
            public FontProperty(PropertyElementMetadata constant) : base(constant)
            {
                Font = new(FontMetadata);
                Style = new(FontStyleMetadata);
                RightToLeft = new(RightToLeftMetadata);
            }
            #endregion

            #region WrapGroup
            public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
            {
                Font,
                Style,
                RightToLeft
            };

            public override void PropertyLoaded()
            {
                Font.ExecuteLoaded(FontMetadata);
                Style.ExecuteLoaded(FontStyleMetadata);
                RightToLeft.ExecuteLoaded(RightToLeftMetadata);
            }

            #endregion


            [DataMember(Order = 0)]
            public Properties.FontProperty Font { get; private set; }

            [DataMember(Order = 1)]
            public SelectorProperty Style { get; private set; }

            [DataMember(Order = 2)]
            public CheckProperty RightToLeft { get; private set; }
        }
    }
}
