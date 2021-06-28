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
using BEditor.Graphics;
using BEditor.Primitive.Resources;

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
        public static readonly DirectProperty<DepthTest, CheckProperty> EnabledProperty = EditingProperty.RegisterDirect<CheckProperty, DepthTest>(
            nameof(Enabled),
            owner => owner.Enabled,
            (owner, obj) => owner.Enabled = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.DepthTestEnable, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Function"/> property.
        /// </summary>
        public static readonly DirectProperty<DepthTest, SelectorProperty> FunctionProperty = EditingProperty.RegisterDirect<SelectorProperty, DepthTest>(
            nameof(Function),
            owner => owner.Function,
            (owner, obj) => owner.Function = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.DepthFunction, new[]
            {
                "Never",
                "Less",
                "Equal",
                "LessEqual",
                "Greater",
                "NotEqual",
                "GreaterEqual",
                "Always",
            }, 1)).Serialize());

        /// <summary>
        /// Defines the <see cref="Mask"/> property.
        /// </summary>
        public static readonly DirectProperty<DepthTest, CheckProperty> MaskProperty = EditingProperty.RegisterDirect<CheckProperty, DepthTest>(
            nameof(Mask),
            owner => owner.Mask,
            (owner, obj) => owner.Mask = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata("Mask", true)).Serialize());

        private static readonly ReadOnlyCollection<ComparisonKind> _depthFunctions = new(new ComparisonKind[]
        {
            ComparisonKind.Never,
            ComparisonKind.Less,
            ComparisonKind.Equal,
            ComparisonKind.LessEqual,
            ComparisonKind.Greater,
            ComparisonKind.NotEqual,
            ComparisonKind.GreaterEqual,
            ComparisonKind.Always,
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

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            Parent.Parent.GraphicsContext!.DepthStencilState.WithDepth(Enabled.Value, Mask.Value, _depthFunctions[Function.Index]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Enabled;
            yield return Function;
            yield return Mask;
        }
    }
}