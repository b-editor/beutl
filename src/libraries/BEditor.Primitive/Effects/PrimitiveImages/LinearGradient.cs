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
using BEditor.LangResources;

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
        public static readonly DirectProperty<LinearGradient, EaseProperty> StartXProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(StartX),
            owner => owner.StartX,
            (owner, obj) => owner.StartX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.StartPoint + " X (%)", 0f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="StartY"/> property.
        /// </summary>
        public static readonly DirectProperty<LinearGradient, EaseProperty> StartYProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(StartY),
            owner => owner.StartY,
            (owner, obj) => owner.StartY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.StartPoint + " Y (%)", 0f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="EndX"/> property.
        /// </summary>
        public static readonly DirectProperty<LinearGradient, EaseProperty> EndXProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(EndX),
            owner => owner.EndX,
            (owner, obj) => owner.EndX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.EndPoint + " X (%)", 100f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="EndY"/> property.
        /// </summary>
        public static readonly DirectProperty<LinearGradient, EaseProperty> EndYProperty = EditingProperty.RegisterDirect<EaseProperty, LinearGradient>(
            nameof(EndY),
            owner => owner.EndY,
            (owner, obj) => owner.EndY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.EndPoint + " Y (%)", 100f, 100f, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Gradient"/> property.
        /// </summary>
        public static readonly DirectProperty<LinearGradient, GradientProperty> GradientProperty = EditingProperty.RegisterDirect<GradientProperty, LinearGradient>(
            nameof(Gradient),
            owner => owner.Gradient,
            (owner, obj) => owner.Gradient = obj,
            EditingPropertyOptions<GradientProperty>.Create(new GradientPropertyMetadata(Strings.Gradient, Colors.Red, Colors.Blue)).Serialize());

        /// <summary>
        /// Defines the <see cref="Mode"/> property.
        /// </summary>
        public static readonly DirectProperty<LinearGradient, SelectorProperty> ModeProperty = EditingProperty.RegisterDirect<SelectorProperty, LinearGradient>(
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
        /// Gets the gradient.
        /// </summary>
        [AllowNull]
        public GradientProperty Gradient { get; private set; }

        /// <summary>
        /// Gets the <see cref="SelectorProperty"/> that selects the gradient mode.
        /// </summary>
        [AllowNull]
        public SelectorProperty Mode { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;

            args.Value.LinerGradient(
                new PointF(StartX[f], StartY[f]),
                new PointF(EndX[f], EndY[f]),
                Gradient.KeyPoints.Select(i => i.Color),
                Gradient.KeyPoints.Select(i => i.Position),
                _tiles[Mode.Index]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return StartX;
            yield return StartY;
            yield return EndX;
            yield return EndY;
            yield return Gradient;
            yield return Mode;
        }

        /// <inheritdoc/>
        public override void SetObjectData(DeserializeContext context)
        {
            base.SetObjectData(context);
            var element = context.Element;

            if (element.TryGetProperty("Colors", out var colors) &&
                element.TryGetProperty("Anchors", out var anchors))
            {
                var colorsArray = colors.GetProperty("Value").GetString()?.Split(',')?.Select(i => Color.Parse(i));
                var anchorsArray = anchors.GetProperty("Value").GetString()?.Split(',')?.Select(i => float.Parse(i));

                if (colorsArray != null && anchorsArray != null)
                {
                    Gradient.KeyPoints.Clear();
                    foreach (var (color, anchor) in colorsArray.Zip(anchorsArray))
                    {
                        Gradient.KeyPoints.Add(new GradientKeyPoint(color, anchor));
                    }
                }
            }
        }
    }
}