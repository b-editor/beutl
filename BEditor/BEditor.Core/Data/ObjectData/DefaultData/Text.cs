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
            public static readonly EasePropertyMetadata SizeMetadata = new EasePropertyMetadata(Properties.Resources.Size, 100, float.NaN, 0);
            public static readonly ColorPropertyMetadata ColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 255, 255, 255);
            public static readonly PropertyElementMetadata FontMetadata = new PropertyElementMetadata(Properties.Resources.DetailedSettings);
            public static readonly DocumentPropertyMetadata DocumentMetadata = new DocumentPropertyMetadata("");

            public Text()
            {
                Size = new EaseProperty(SizeMetadata);
                Color = new ColorProperty(ColorMetadata);
                Font = new FontProperty(FontMetadata);
                Document = new DocumentProperty(DocumentMetadata);
            }


            #region DefaultImageObjectメンバー
            public override Media.Image Load(EffectRenderArgs args) => Media.Image.Text(
                (int)Size.GetValue(args.Frame),
                Color,
                Document.Text,
                Font.Font.Select,
                (string)Font.Style.SelectItem,
                Font.RightToLeft.IsChecked);

            public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
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
                public static readonly FontPropertyMetadata FontMetadata = new FontPropertyMetadata();
                public static readonly SelectorPropertyMetadata FontStyleMetadata = new SelectorPropertyMetadata(Properties.Resources.FontStyles,
                                                                                                          PropertyData.FontProperty.FontStylesList);
                public static readonly CheckPropertyMetadata RightToLeftMetadata = new CheckPropertyMetadata("RightToLeft", false);

                #region コンストラクタ
                public FontProperty(PropertyElementMetadata constant) : base(constant)
                {
                    Font = new(FontMetadata);
                    Style = new(FontStyleMetadata);
                    RightToLeft = new(RightToLeftMetadata);
                }
                #endregion

                #region WrapGroup
                public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
                    Font,
                    Style,
                    RightToLeft
                };

                #endregion


                [DataMember(Order = 0)]
                [PropertyMetadata(nameof(FontMetadata), typeof(FontProperty))]
                public PropertyData.FontProperty Font { get; set; }

                [DataMember(Order = 1)]
                [PropertyMetadata(nameof(FontStyleMetadata), typeof(FontProperty))]
                public SelectorProperty Style { get; set; }

                [DataMember(Order = 2)]
                [PropertyMetadata(nameof(RightToLeftMetadata), typeof(FontProperty))]
                public CheckProperty RightToLeft { get; set; }
            }
        }
    }
}
