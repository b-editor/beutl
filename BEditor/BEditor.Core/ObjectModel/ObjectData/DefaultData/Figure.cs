using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;

namespace BEditor.ObjectModel.ObjectData
{
    public static partial class DefaultData
    {
        [DataContract(Namespace = "")]
        public class Figure : DefaultImageObject
        {
            public static readonly EasePropertyMetadata WidthMetadata = new(Core.Properties.Resources.Width, 100, float.NaN, 0);
            public static readonly EasePropertyMetadata HeightMetadata = new(Core.Properties.Resources.Height, 100, float.NaN, 0);
            public static readonly EasePropertyMetadata LineMetadata = new(Core.Properties.Resources.Line, 4000, float.NaN, 0);
            public static readonly ColorPropertyMetadata ColorMetadata = new(Core.Properties.Resources.Color, 255, 255, 255);
            public static readonly SelectorPropertyMetadata TypeMetadata = new(
                Core.Properties.Resources.Type,
                new string[]
                {
                    Core.Properties.Resources.Circle,
                    Core.Properties.Resources.Square
                });

            public Figure()
            {
                Width = new(WidthMetadata);
                Height = new(HeightMetadata);
                Line = new(LineMetadata);
                Color = new(ColorMetadata);
                Type = new(TypeMetadata);
            }



            #region DefaultImageObjectメンバー
            public override Media.Image Render(EffectRenderArgs args)
            {
                if (Type.Index == 0)
                {
                    return Media.Image.Ellipse((int)Width.GetValue(args.Frame), (int)Height.GetValue(args.Frame), (int)Line.GetValue(args.Frame), Color);
                }
                else
                {
                    return Media.Image.Rectangle((int)Width.GetValue(args.Frame), (int)Height.GetValue(args.Frame), (int)Line.GetValue(args.Frame), Color);
                }
            }

            public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
            {
                Width,
                Height,
                Line,
                Color,
                Type
            };

            #endregion


            [DataMember(Order = 0)]
            [PropertyMetadata(nameof(WidthMetadata), typeof(Figure))]
            public EaseProperty Width { get; private set; }

            [DataMember(Order = 1)]
            [PropertyMetadata(nameof(HeightMetadata), typeof(Figure))]
            public EaseProperty Height { get; private set; }

            [DataMember(Order = 2)]
            [PropertyMetadata(nameof(LineMetadata), typeof(Figure))]
            public EaseProperty Line { get; private set; }

            [DataMember(Order = 3)]
            [PropertyMetadata(nameof(ColorMetadata), typeof(Figure))]
            public ColorProperty Color { get; private set; }

            [DataMember(Order = 4)]
            [PropertyMetadata(nameof(TypeMetadata), typeof(Figure))]
            public SelectorProperty Type { get; private set; }
        }
    }
}
