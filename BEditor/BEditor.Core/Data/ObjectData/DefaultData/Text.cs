using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Extensions;

using DocumentProperty = BEditor.Core.Data.PropertyData.DocumentProperty;

namespace BEditor.Core.Data.ObjectData
{
    public static partial class DefaultData
    {
        [DataContract(Namespace = "")]
        public class Text : ImageObject
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
            public override Media.Image OnRender(EffectRenderArgs args) => Media.Image.Text(
                (int)Size.GetValue(args.Frame),
                Color.Color,
                Document.Text,
                Font.Font.Select,
                (string)Font.Style.SelectItem,
                Font.RightToLeft.IsChecked);

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

            public override void PropertyLoaded()
            {
                Size.ExecuteLoaded(SizeMetadata);
                Color.ExecuteLoaded(ColorMetadata);
                Font.ExecuteLoaded(FontMetadata);
                Document.ExecuteLoaded(DocumentMetadata);
            }

            #endregion



            [DataMember(Order = 0)]
            public EaseProperty Size { get; private set; }

            [DataMember(Order = 1)]
            public ColorProperty Color { get; private set; }

            [DataMember(Order = 2)]
            public DocumentProperty Document { get; private set; }

            [DataMember(Order = 3)]
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

                public override void PropertyLoaded()
                {
                    Font.ExecuteLoaded(FontMetadata);
                    Style.ExecuteLoaded(FontStyleMetadata);
                    RightToLeft.ExecuteLoaded(RightToLeftMetadata);
                }

                #endregion


                [DataMember(Order = 0)]
                public PropertyData.FontProperty Font { get; private set; }

                [DataMember(Order = 1)]
                public SelectorProperty Style { get; private set; }

                [DataMember(Order = 2)]
                public CheckProperty RightToLeft { get; private set; }
            }
        }
    }
}
