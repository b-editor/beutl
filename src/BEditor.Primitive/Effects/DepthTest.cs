using System.Collections.Generic;
using System.Collections.ObjectModel;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Primitive.Resources;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="EffectElement"/> that sets the OpenGL depth test.
    /// </summary>
    public sealed class DepthTest : EffectElement
    {
        /// <summary>
        /// Defines the <see cref="Enabled"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, CheckProperty> EnabledProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, DepthTest>(
            nameof(Enabled),
            owner => owner.Enabled,
            (owner, obj) => owner.Enabled = obj,
            new CheckPropertyMetadata(Strings.DepthTestEnable, true));

        /// <summary>
        /// Defines the <see cref="Function"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, SelectorProperty> FunctionProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty, DepthTest>(
            nameof(Function),
            owner => owner.Function,
            (owner, obj) => owner.Function = obj,
            new SelectorPropertyMetadata(Strings.DepthFunction, new[]
            {
                "Never",
                "Less",
                "Equal",
                "Lequal",
                "Greater",
                "Notequal",
                "Gequal",
                "Always"
            }, 1));

        /// <summary>
        /// Defines the <see cref="Mask"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, CheckProperty> MaskProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, DepthTest>(
            nameof(Mask),
            owner => owner.Mask,
            (owner, obj) => owner.Mask = obj,
            new CheckPropertyMetadata("Mask", true));

        /// <summary>
        /// Defines the <see cref="Near"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, EaseProperty> NearProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, DepthTest>(
            nameof(Near),
            owner => owner.Near,
            (owner, obj) => owner.Near = obj,
            new EasePropertyMetadata("Near", 0, 100, 0));

        /// <summary>
        /// Defines the <see cref="Far"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, EaseProperty> FarProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, DepthTest>(
            nameof(Far),
            owner => owner.Far,
            (owner, obj) => owner.Far = obj,
            new EasePropertyMetadata("Far", 100, 100, 0));

        private static readonly ReadOnlyCollection<DepthFunction> DepthFunctions = new(new DepthFunction[]
        {
            DepthFunction.Never,
            DepthFunction.Less,
            DepthFunction.Equal,
            DepthFunction.Lequal,
            DepthFunction.Greater,
            DepthFunction.Notequal,
            DepthFunction.Gequal,
            DepthFunction.Always
        });

        /// <summary>
        /// Initializes a new instance of the <see cref="DepthTest"/> class.
        /// </summary>
#pragma warning disable CS8618
        public DepthTest()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.DepthTest;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Enabled;
                yield return Function;
                yield return Mask;
                yield return Near;
                yield return Far;
            }
        }

        /// <summary>
        /// Gets the value to enable depth testing.
        /// </summary>
        public CheckProperty Enabled { get; private set; }

        /// <summary>
        /// Get the <see cref="SelectorProperty"/> that selects the function for the depth test.
        /// </summary>
        public SelectorProperty Function { get; private set; }

        /// <summary>
        /// Gets whether the depth mask is enabled.
        /// </summary>
        public CheckProperty Mask { get; private set; }

        /// <summary>
        /// Gets the depth range.
        /// </summary>
        public EaseProperty Near { get; private set; }

        /// <summary>
        /// Gets the depth range.
        /// </summary>
        public EaseProperty Far { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            if (Enabled.Value) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc(DepthFunctions[Function.Index]);

            GL.DepthMask(Mask.Value);

            GL.DepthRange(Near.GetValue(args.Frame) / 100, Far.GetValue(args.Frame) / 100);
        }
    }
}