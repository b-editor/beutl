// LinearGradient.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

using Reactive.Bindings;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that masks an image with a linear gradient.
    /// </summary>
    public sealed class LinearGradient : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="StartX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, EaseProperty> StartXProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(StartX),
            owner => owner.StartX,
            (owner, obj) => owner.StartX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.StartPoint + " X (%)", 0f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="StartY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, EaseProperty> StartYProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(StartY),
            owner => owner.StartY,
            (owner, obj) => owner.StartY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.StartPoint + " Y (%)", 0f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="EndX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, EaseProperty> EndXProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(EndX),
            owner => owner.EndX,
            (owner, obj) => owner.EndX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.EndPoint + " X (%)", 100f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="EndY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, EaseProperty> EndYProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(EndY),
            owner => owner.EndY,
            (owner, obj) => owner.EndY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.EndPoint + " Y (%)", 100f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Colors"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, TextProperty> ColorsProperty = EditingProperty.RegisterDirect<TextProperty, LinearGradient>(
            nameof(Colors),
            owner => owner.Colors,
            (owner, obj) => owner.Colors = obj,
            EditingPropertyOptions<TextProperty>.Create(new TextPropertyMetadata(Strings.Colors, "#FFFF0000,#FF0000FF")).Serialize());

        /// <summary>
        /// Defines the <see cref="Anchors"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, TextProperty> AnchorsProperty = EditingProperty.RegisterDirect<TextProperty, LinearGradient>(
            nameof(Anchors),
            owner => owner.Anchors,
            (owner, obj) => owner.Anchors = obj,
            EditingPropertyOptions<TextProperty>.Create(new TextPropertyMetadata(Strings.Anchors, "0,1")).Serialize());

        /// <summary>
        /// Defines the <see cref="Mode"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LinearGradient, SelectorProperty> ModeProperty = EditingProperty.RegisterDirect<SelectorProperty, LinearGradient>(
            nameof(Mode),
            owner => owner.Mode,
            (owner, obj) => owner.Mode = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Mode, new string[] { Strings.Clamp, Strings.Repeat, Strings.Mirror, Strings.Decal }, 1)).Serialize());

        internal static readonly ShaderTileMode[] _tiles =
        {
            ShaderTileMode.Clamp,
            ShaderTileMode.Repeat,
            ShaderTileMode.Mirror,
            ShaderTileMode.Decal,
        };

        private ReactiveProperty<Color[]>? _colorsProp;

        private ReactiveProperty<float[]>? _pointsProp;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearGradient"/> class.
        /// </summary>
        public LinearGradient()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.LinearGradient;

        /// <summary>
        /// Gets the start position of the X axis.
        /// </summary>
        [AllowNull]
        public EaseProperty StartX { get; private set; }

        /// <summary>
        /// Gets the start position of the Y axis.
        /// </summary>
        [AllowNull]
        public EaseProperty StartY { get; private set; }

        /// <summary>
        /// Gets the end position of the X axis.
        /// </summary>
        [AllowNull]
        public EaseProperty EndX { get; private set; }

        /// <summary>
        /// Gets the end position of the Y axis.
        /// </summary>
        [AllowNull]
        public EaseProperty EndY { get; private set; }

        /// <summary>
        /// Gets the colors.
        /// </summary>
        [AllowNull]
        public TextProperty Colors { get; private set; }

        /// <summary>
        /// Gets the anchors.
        /// </summary>
        [AllowNull]
        public TextProperty Anchors { get; private set; }

        /// <summary>
        /// Gets the <see cref="SelectorProperty"/> that selects the gradient mode.
        /// </summary>
        [AllowNull]
        public SelectorProperty Mode { get; private set; }

        private ReactiveProperty<Color[]> ColorsProp => _colorsProp ??= new();

        private ReactiveProperty<float[]> PointsProp => _pointsProp ??= new();

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;
            var colors = ColorsProp.Value;
            var points = PointsProp.Value;

            // 色とアンカーの長さが合わない場合
            // どちらかに長さを合わせる
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
                new PointF(StartX[f], StartY[f]),
                new PointF(EndX[f], EndY[f]),
                colors,
                points,
                _tiles[Mode.Index]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return StartX;
            yield return StartY;
            yield return EndX;
            yield return EndY;
            yield return Colors;
            yield return Anchors;
            yield return Mode;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _colorsProp = Colors
                .Select(str =>
                    str.Replace(" ", string.Empty)
                        .Split(',')
                        .Select(s => Color.FromHTML(s))
                        .ToArray())
                .ToReactiveProperty()!;

            _pointsProp = Anchors
                .Select(str =>
                    str.Replace(" ", string.Empty)
                        .Split(',')
                        .Where(s => float.TryParse(s, out _))
                        .Select(s => float.Parse(s))
                        .ToArray())
                .ToReactiveProperty()!;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _colorsProp?.Dispose();
            _pointsProp?.Dispose();
        }
    }
}