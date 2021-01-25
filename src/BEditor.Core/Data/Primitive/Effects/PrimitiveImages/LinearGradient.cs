using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using Reactive.Bindings;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class LinearGradient : ImageEffect
    {
        public static readonly EasePropertyMetadata StartXMetadata = new(Resources.StartPoint + " X (%)", 0f, 100f, 0);
        public static readonly EasePropertyMetadata StartYMetadata = StartXMetadata with { Name = Resources.StartPoint + " Y (%)" };
        public static readonly EasePropertyMetadata EndXMetadata = StartXMetadata with { Name = Resources.EndPoint + " X (%)", DefaultValue = 100f };
        public static readonly EasePropertyMetadata EndYMetadata = EndXMetadata with { Name = Resources.EndPoint + " Y (%)" };
        public static readonly TextPropertyMetadata ColorsMetadata = new(Resources.Colors, "#FFFF0000,#FF0000FF");
        public static readonly TextPropertyMetadata AnchorsMetadata = new(Resources.Anchors, "0,1");
        public static readonly SelectorPropertyMetadata ModeMetadata = new(Resources.Mode, new string[] { Resources.Repeat });
        private ReactiveProperty<Color[]>? _colorsProp;
        private ReactiveProperty<float[]>? _pointsProp;

        public LinearGradient()
        {
            StartX = new(StartXMetadata);
            StartY = new(StartYMetadata);
            EndX = new(EndXMetadata);
            EndY = new(EndYMetadata);
            Colors = new(ColorsMetadata);
            Anchors = new(AnchorsMetadata);
        }

        public override string Name => Resources.LinearGradient;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            StartX,
            StartY,
            EndX,
            EndY,
            Colors,
            Anchors
        };
        [DataMember(Order = 0)]
        public EaseProperty StartX { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty StartY { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty EndX { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty EndY { get; private set; }
        [DataMember(Order = 4)]
        public TextProperty Colors { get; private set; }
        [DataMember(Order = 5)]
        public TextProperty Anchors { get; private set; }

        private ReactiveProperty<Color[]> ColorsProp => _colorsProp ??= new();
        private ReactiveProperty<float[]> PointsProp => _pointsProp ??= new();

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var w = args.Value.Width;
            var h = args.Value.Height;
            var f = args.Frame;
            var st = new PointF(StartX[f] * w * 0.01f, StartY[f] * h * 0.01f);
            var ed = new PointF(EndX[f] * w * 0.01f, EndY[f] * h * 0.01f);
            var colors = ColorsProp.Value;
            var points = PointsProp.Value;

            // 非推奨
            while (colors.Length != points.Length)
            {
                if (colors.Length < points.Length)
                {
                    colors = colors.Append(default).ToArray();
                }
                else if (colors.Length > points.Length)
                {
                    points = points.Append(default).ToArray();
                }
            }

            args.Value.LinerGradient(
                st,
                ed,
                colors,
                points);
        }
        protected override void OnLoad()
        {
            StartX.Load(StartXMetadata);
            StartY.Load(StartYMetadata);
            EndX.Load(EndXMetadata);
            EndY.Load(EndYMetadata);
            Colors.Load(ColorsMetadata);
            Anchors.Load(AnchorsMetadata);

            _colorsProp = Colors
                .Select(str =>
                    str.Replace(" ", "")
                        .Split(',')
                        .Select(s => Color.FromHTML(s))
                        .ToArray())
                .ToReactiveProperty()!;

            _pointsProp = Anchors
                .Select(str =>
                    str.Replace(" ", "")
                        .Split(',')
                        .Where(s => float.TryParse(s, out _))
                        .Select(s => float.Parse(s))
                        .ToArray())
                .ToReactiveProperty()!;

            Colors.Value += " ";
            Anchors.Value += " ";
        }
        protected override void OnUnload()
        {
            foreach (var p in Children)
            {
                p.Unload();
            }

            _colorsProp?.Dispose();
            _pointsProp?.Dispose();
        }
    }
}
