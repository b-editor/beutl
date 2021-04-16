using System;
using System.Collections.Generic;

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
        /// Represents <see cref="Opacity"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata OpacityMetadata = new(Strings.Opacity, 100, 100, 0);

        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorAnimationPropertyMetadata ColorMetadata = new(Strings.Color, Drawing.Color.Light, false);

        /// <summary>
        /// Represents <see cref="BlendType"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata BlendTypeMetadata = new(Strings.Blend, new[] { "通常", "加算", "減算", "乗算" });

        /// <summary>
        /// OpenGLの合成方法を設定する <see cref="Action"/> です.
        /// </summary>
        internal static readonly Action[] BlentFunc = new Action[]
        {
            () =>
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
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
        public Blend(PropertyElementMetadata metadata)
            : base(metadata)
        {
            Opacity = new(OpacityMetadata);
            BlendType = new(BlendTypeMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Opacity,
            Color,
            BlendType,
        };

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the transparency.
        /// </summary>
        [DataMember]
        public EaseProperty Opacity { get; private set; }

        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> that represents a color.
        /// </summary>
        [DataMember]
        public ColorAnimationProperty Color { get; private set; }

        /// <summary>
        /// Gets the <see cref="SelectorProperty"/> that selects the BlendFunc.
        /// </summary>
        [DataMember]
        public SelectorProperty BlendType { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Opacity.Load(OpacityMetadata);
            BlendType.Load(BlendTypeMetadata);
            Color.Load(ColorMetadata);
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Opacity.Unload();
            BlendType.Unload();
            Color.Unload();
        }
    }
}