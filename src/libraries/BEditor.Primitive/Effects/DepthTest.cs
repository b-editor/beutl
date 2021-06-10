// DepthTest.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

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
        public static readonly DirectEditingProperty<DepthTest, CheckProperty> EnabledProperty = EditingProperty.RegisterDirect<CheckProperty, DepthTest>(
            nameof(Enabled),
            owner => owner.Enabled,
            (owner, obj) => owner.Enabled = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.DepthTestEnable, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Function"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, SelectorProperty> FunctionProperty = EditingProperty.RegisterDirect<SelectorProperty, DepthTest>(
            nameof(Function),
            owner => owner.Function,
            (owner, obj) => owner.Function = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.DepthFunction, new[]
            {
                "Never",
                "Less",
                "Equal",
                "Lequal",
                "Greater",
                "Notequal",
                "Gequal",
                "Always",
            }, 1)).Serialize());

        /// <summary>
        /// Defines the <see cref="Mask"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, CheckProperty> MaskProperty = EditingProperty.RegisterDirect<CheckProperty, DepthTest>(
            nameof(Mask),
            owner => owner.Mask,
            (owner, obj) => owner.Mask = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata("Mask", true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Near"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, EaseProperty> NearProperty = EditingProperty.RegisterDirect<EaseProperty, DepthTest>(
            nameof(Near),
            owner => owner.Near,
            (owner, obj) => owner.Near = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Near", 0, 100, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Far"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<DepthTest, EaseProperty> FarProperty = EditingProperty.RegisterDirect<EaseProperty, DepthTest>(
            nameof(Far),
            owner => owner.Far,
            (owner, obj) => owner.Far = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Far", 100, 100, 0)).Serialize());

        private static readonly ReadOnlyCollection<DepthFunction> DepthFunctions = new(new DepthFunction[]
        {
            DepthFunction.Never,
            DepthFunction.Less,
            DepthFunction.Equal,
            DepthFunction.Lequal,
            DepthFunction.Greater,
            DepthFunction.Notequal,
            DepthFunction.Gequal,
            DepthFunction.Always,
        });

        /// <summary>
        /// Initializes a new instance of the <see cref="DepthTest"/> class.
        /// </summary>
        public DepthTest()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.DepthTest;

        /// <summary>
        /// Gets the value to enable depth testing.
        /// </summary>
        [AllowNull]
        public CheckProperty Enabled { get; private set; }

        /// <summary>
        /// Get the <see cref="SelectorProperty"/> that selects the function for the depth test.
        /// </summary>
        [AllowNull]
        public SelectorProperty Function { get; private set; }

        /// <summary>
        /// Gets whether the depth mask is enabled.
        /// </summary>
        [AllowNull]
        public CheckProperty Mask { get; private set; }

        /// <summary>
        /// Gets the depth range.
        /// </summary>
        [AllowNull]
        public EaseProperty Near { get; private set; }

        /// <summary>
        /// Gets the depth range.
        /// </summary>
        [AllowNull]
        public EaseProperty Far { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            if (Enabled.Value) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc(DepthFunctions[Function.Index]);

            GL.DepthMask(Mask.Value);

            GL.DepthRange(Near.GetValue(args.Frame) / 100, Far.GetValue(args.Frame) / 100);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Enabled;
            yield return Function;
            yield return Mask;
            yield return Near;
            yield return Far;
        }
    }
}