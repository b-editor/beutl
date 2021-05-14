using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Resources;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting the blend Option.
    /// </summary>
    public sealed class Blend : ExpandGroup
    {
        /// <summary>
        /// Defines the <see cref="Opacity"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blend, EaseProperty> OpacityProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Blend>(
            nameof(Opacity),
            owner => owner.Opacity,
            (owner, obj) => owner.Opacity = obj,
            new EasePropertyMetadata(Strings.Opacity, 100, 100, 0, UseOptional: true));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blend, ColorAnimationProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorAnimationProperty, Blend>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            new ColorAnimationPropertyMetadata(Strings.Color, Drawing.Color.Light, false));

        /// <summary>
        /// Defines the <see cref="BlendType"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blend, SelectorProperty> BlendTypeProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty, Blend>(
            nameof(BlendType),
            owner => owner.BlendType,
            (owner, obj) => owner.BlendType = obj,
            new SelectorPropertyMetadata(Strings.Blend, new[] { "通常", "加算", "減算", "乗算" }));

        /// <summary>
        /// OpenGLの合成方法を設定する <see cref="Action"/> です.
        /// </summary>
        internal static readonly Action[] BlentFunc = new Action[]
        {
            () =>
            {
                GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            },
            () =>
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            },
            () =>
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncReverseSubtract, BlendEquationMode.FuncReverseSubtract);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            },
            () =>
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.SrcColor);
            },
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Blend"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Blend(BlendMetadata metadata) : base(metadata)
        {
        }

        /// <summary>
        /// Gets the opacity.
        /// </summary>
        [AllowNull]
        public EaseProperty Opacity { get; private set; }

        /// <summary>
        /// Gets the color.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Color { get; private set; }

        /// <summary>
        /// Gets the BlendFunc.
        /// </summary>
        [AllowNull]
        public SelectorProperty BlendType { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Opacity;
            yield return Color;
            yield return BlendType;
        }

        /// <summary>
        /// Reset the <see cref="Opacity"/> Optionals.
        /// </summary>
        public void ResetOptional()
        {
            Opacity.Optional = 0;
        }
    }

    /// <summary>
    /// The metadata of <see cref="Blend"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record BlendMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<Blend>
    {
        /// <inheritdoc/>
        public Blend Create()
        {
            return new(this);
        }
    }
}