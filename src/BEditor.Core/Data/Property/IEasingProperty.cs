// IEasingProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Data.Property.Easing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property that can be used by <see cref="EasingFunc"/>.
    /// </summary>
    public interface IEasingProperty : IPropertyElement
    {
        /// <inheritdoc cref="IChild{T}.Parent"/>
        public new EffectElement Parent { get; set; }
    }
}