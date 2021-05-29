// ObjectElement.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents a base class of the object.
    /// </summary>
    public abstract class ObjectElement : EffectElement
    {
        /// <summary>
        /// Filter a effect.
        /// </summary>
        /// <param name="effect">The effect to see if they should be added.</param>
        /// <returns>Returns <see langword="true"/> if this effect is to be added, <see langword="false"/> if not.</returns>
        public virtual bool EffectFilter(EffectElement effect) => true;
    }
}