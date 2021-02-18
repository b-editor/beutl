using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data.Property;
using BEditor.Properties;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting the blend Option.
    /// </summary>
    [DataContract]
    public sealed class Blend : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="Alpha"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AlphaMetadata = new(Resources.Alpha, 100, 100, 0);
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorAnimationPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light, false);
        /// <summary>
        /// Represents <see cref="BlendType"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata BlendTypeMetadata = new(Resources.Blend, new string[4] { "通常", "加算", "減算", "乗算" });
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
            }
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Blend"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Blend(PropertyElementMetadata metadata) : base(metadata)
        {
            Alpha = new(AlphaMetadata);
            BlendType = new(BlendTypeMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Alpha,
            Color,
            BlendType
        };
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the transparency.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty Alpha { get; private set; }
        /// <summary>
        /// Get a <see cref="ColorAnimationProperty"/> that represents a color.
        /// </summary>
        [DataMember(Order = 1)]
        public ColorAnimationProperty Color { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> that selects the BlendFunc.
        /// </summary>
        [DataMember(Order = 2)]
        public SelectorProperty BlendType { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Alpha.Load(AlphaMetadata);
            BlendType.Load(BlendTypeMetadata);
            Color.Load(ColorMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Alpha.Unload();
            BlendType.Unload();
            Color.Unload();
        }
    }
}
