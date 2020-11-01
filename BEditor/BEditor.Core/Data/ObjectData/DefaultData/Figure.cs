using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.ObjectData {
    public partial class DefaultData {
        [DataContract(Namespace = "")]
        public class Figure : DefaultImageObject {
            public static readonly EasePropertyMetadata WidthMetadata = new EasePropertyMetadata(Properties.Resources.Width, 100, float.NaN, 0);
            public static readonly EasePropertyMetadata HeightMetadata = new EasePropertyMetadata(Properties.Resources.Height, 100, float.NaN, 0);
            public static readonly EasePropertyMetadata LineMetadata = new EasePropertyMetadata(Properties.Resources.Line, 4000, float.NaN, 0);
            public static readonly ColorPropertyMetadata ColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 255, 255, 255);
            public static readonly SelectorPropertyMetadata TypeMetadata = new SelectorPropertyMetadata(Properties.Resources.Type,
                                                                                                 0,
                                                                                                 new string[2] {
                                                                                                     Properties.Resources.Circle,
                                                                                                     Properties.Resources.Square
                                                                                                 });

            public Figure() {
                Width = new EaseProperty(WidthMetadata);
                Height = new EaseProperty(HeightMetadata);
                Line = new EaseProperty(LineMetadata);
                Color = new ColorProperty(ColorMetadata);
                Type = new SelectorProperty(TypeMetadata);
            }



            #region DefaultImageObjectメンバー
            public override Media.Image Load(EffectLoadArgs args) {
                if (Type.Index == 0) {
                    return Media.Image.Ellipse((int)Width.GetValue(args.Frame), (int)Height.GetValue(args.Frame), (int)Line.GetValue(args.Frame), Color);
                }
                else {
                    return Media.Image.Rectangle((int)Width.GetValue(args.Frame), (int)Height.GetValue(args.Frame), (int)Line.GetValue(args.Frame), Color);
                }
            }

            public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
                Width,
                Height,
                Line,
                Color,
                Type
            };

            #endregion


            [DataMember(Order = 0)]
            [PropertyMetadata(nameof(WidthMetadata), typeof(Figure))]
            public EaseProperty Width { get; set; }

            [DataMember(Order = 1)]
            [PropertyMetadata(nameof(HeightMetadata), typeof(Figure))]
            public EaseProperty Height { get; set; }

            [DataMember(Order = 2)]
            [PropertyMetadata(nameof(LineMetadata), typeof(Figure))]
            public EaseProperty Line { get; set; }

            [DataMember(Order = 3)]
            [PropertyMetadata(nameof(ColorMetadata), typeof(Figure))]
            public ColorProperty Color { get; set; }

            [DataMember(Order = 4)]
            [PropertyMetadata(nameof(TypeMetadata), typeof(Figure))]
            public SelectorProperty Type { get; set; }
        }
    }
}
