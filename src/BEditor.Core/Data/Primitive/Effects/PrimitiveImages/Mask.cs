using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.PrimitiveGroup;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using Reactive.Bindings;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class Mask : ImageEffect
    {
        public static readonly EasePropertyMetadata XMetadata = Coordinate.XMetadata;
        public static readonly EasePropertyMetadata YMetadata = Coordinate.YMetadata;
        public static readonly EasePropertyMetadata RotateMetadata = new(Resources.Rotate);
        public static readonly EasePropertyMetadata WidthMetadata = Figure.WidthMetadata;
        public static readonly EasePropertyMetadata HeightMetadata = Figure.HeightMetadata;
        public static readonly TextPropertyMetadata ImageMetadata = new("画像へのパス");
        //Todo: 英語対応
        public static readonly CheckPropertyMetadata ReverseMetadata = new("マスクの反転");
        public static readonly CheckPropertyMetadata FitSizeMetadata = new("元のサイズに合わせる");
        private ReactiveProperty<ClipData?>? _clipProperty;

        public Mask()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Rotate = new(RotateMetadata);
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Image = new(ImageMetadata);
            Reverse = new(ReverseMetadata);
            FitSize = new(FitSizeMetadata);
        }

        public override string Name => Resources.Mask;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Rotate,
            Width,
            Height,
            Image,
            Reverse,
            FitSize
        };
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Rotate { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty Width { get; private set; }
        [DataMember(Order = 4)]
        public EaseProperty Height { get; private set; }
        [DataMember(Order = 5)]
        public TextProperty Image { get; private set; }
        [DataMember(Order = 6)]
        public CheckProperty Reverse { get; private set; }
        [DataMember(Order = 7)]
        public CheckProperty FitSize { get; private set; }
        private ReactiveProperty<ClipData?> ClipProperty => _clipProperty ??= new();

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {


            //args.Value.Mask()
        }
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Rotate.Load(RotateMetadata);
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Image.Load(ImageMetadata);
            Reverse.Load(ReverseMetadata);
            FitSize.Load(FitSizeMetadata);

            _clipProperty = Image
                .Select(str => ClipData.FromFullName(str, Parent?.Parent?.Parent))
                .ToReactiveProperty();

            ClipProperty.Value = ClipData.FromFullName(Image.Value, Parent?.Parent?.Parent);
        }
        protected override void OnUnload()
        {
            foreach (var p in Children)
            {
                p.Unload();
            }
        }
    }
}
