using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that masks an image with a circular gradient.
    /// </summary>
    public sealed class CircularGradient : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="CenterX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterXMetadata = new(Resources.CenterX, 0);
        /// <summary>
        /// Represents <see cref="CenterY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterYMetadata = new(Resources.CenterY, 0);
        /// <summary>
        /// Represents <see cref="Radius"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RadiusMetadata = new(Resources.Radius, 100);
        /// <summary>
        /// Represents <see cref="Colors"/> metadata.
        /// </summary>
        public static readonly TextPropertyMetadata ColorsMetadata = new(Resources.Colors, "#FFFF0000,#FF0000FF");
        /// <summary>
        /// Represents <see cref="Anchors"/> metadata.
        /// </summary>
        public static readonly TextPropertyMetadata AnchorsMetadata = new(Resources.Anchors, "0,1");
        /// <summary>
        /// Represents <see cref="Mode"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata ModeMetadata = LinearGradient.ModeMetadata;
        private ReactiveProperty<Color[]>? _colorsProp;
        private ReactiveProperty<float[]>? _pointsProp;

        /// <summary>
        /// Initializes a new instance of the <see cref="CircularGradient"/> class.
        /// </summary>
        public CircularGradient()
        {
            CenterX = new(CenterXMetadata);
            CenterY = new(CenterYMetadata);
            Radius = new(RadiusMetadata);
            Colors = new(ColorsMetadata);
            Anchors = new(AnchorsMetadata);
            Mode = new(ModeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.CircularGradient;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            CenterX,
            CenterY,
            Radius,
            Colors,
            Anchors,
            Mode
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate of the center.
        /// </summary>
        [DataMember]
        public EaseProperty CenterX { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate of the center.
        /// </summary>
        [DataMember]
        public EaseProperty CenterY { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the radius.
        /// </summary>
        [DataMember]
        public EaseProperty Radius { get; private set; }
        /// <summary>
        /// Get the <see cref="TextProperty"/> representing the colors.
        /// </summary>
        [DataMember]
        public TextProperty Colors { get; private set; }
        /// <summary>
        /// Get the <see cref="TextProperty"/> representing the anchors.
        /// </summary>
        [DataMember]
        public TextProperty Anchors { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> that selects the gradient mode.
        /// </summary>
        [DataMember]
        public SelectorProperty Mode { get; private set; }

        private ReactiveProperty<Color[]> ColorsProp => _colorsProp ??= new();
        private ReactiveProperty<float[]> PointsProp => _pointsProp ??= new();

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;
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

            args.Value.CircularGradient(
                new PointF(CenterX[f], CenterY[f]),
                Radius[f],
                colors,
                points,
                LinearGradient.tiles[Mode.Index]);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            CenterX.Load(CenterXMetadata);
            CenterY.Load(CenterYMetadata);
            Radius.Load(RadiusMetadata);
            Colors.Load(ColorsMetadata);
            Anchors.Load(AnchorsMetadata);
            Mode.Load(ModeMetadata);

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
        }
        /// <inheritdoc/>
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
