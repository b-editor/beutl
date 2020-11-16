using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;

using DocumentProperty = BEditor.Core.Data.PropertyData.DocumentProperty;

namespace BEditor.Core.Data.ObjectData
{
    public static partial class DefaultData
    {
        [DataContract(Namespace = "")]
        public class Text : DefaultImageObject
        {
            public static readonly EasePropertyMetadata SizeMetadata = new(Core.Properties.Resources.Size, 100, float.NaN, 0);
            public static readonly ColorPropertyMetadata ColorMetadata = new(Core.Properties.Resources.Color, 255, 255, 255);
            public static readonly PropertyElementMetadata FontMetadata = new(Core.Properties.Resources.DetailedSettings);
            public static readonly DocumentPropertyMetadata DocumentMetadata = new("");

            public Text()
            {
                Size = new(SizeMetadata);
                Color = new(ColorMetadata);
                Font = new(FontMetadata);
                Document = new(DocumentMetadata);
            }


            #region DefaultImageObjectメンバー
            public override Media.Image Render(EffectRenderArgs args) => Media.Image.Text(
                (int)Size.GetValue(args.Frame),
                Color,
                Document.Text,
                Font.Font.Select,
                (string)Font.Style.SelectItem,
                Font.RightToLeft.IsChecked);

            public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
            {
                Size,
                Color,
                Document,
                Font
            };

            #endregion



            [DataMember(Order = 0)]
            [PropertyMetadata(nameof(SizeMetadata), typeof(Text))]
            public EaseProperty Size { get; private set; }

            [DataMember(Order = 1)]
            [PropertyMetadata(nameof(ColorMetadata), typeof(Text))]
            public ColorProperty Color { get; private set; }

            [DataMember(Order = 2)]
            [PropertyMetadata(nameof(DocumentMetadata), typeof(Text))]
            public DocumentProperty Document { get; private set; }

            [DataMember(Order = 3)]
            [PropertyMetadata(nameof(FontMetadata), typeof(Text))]
            public FontProperty Font { get; private set; }


            [DataContract(Namespace = "")]
            public class FontProperty : ExpandGroup
            {
                public static readonly FontPropertyMetadata FontMetadata = new();
                public static readonly SelectorPropertyMetadata FontStyleMetadata = new(Core.Properties.Resources.FontStyles,
                                                                                                          PropertyData.FontProperty.FontStylesList);
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

                #endregion


                [DataMember(Order = 0)]
                [PropertyMetadata(nameof(FontMetadata), typeof(FontProperty))]
                public PropertyData.FontProperty Font { get; private set; }

                [DataMember(Order = 1)]
                [PropertyMetadata(nameof(FontStyleMetadata), typeof(FontProperty))]
                public SelectorProperty Style { get; private set; }

                [DataMember(Order = 2)]
                [PropertyMetadata(nameof(RightToLeftMetadata), typeof(FontProperty))]
                public CheckProperty RightToLeft { get; private set; }
            }
        }
    }
}
