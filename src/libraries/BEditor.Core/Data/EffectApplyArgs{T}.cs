// EffectApplyArgs{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents data that is passed to <see cref="EffectElement"/> when it is applied.
    /// </summary>
    /// <typeparam name="T">The type of value to pass to the <see cref="EffectElement.Apply(EffectApplyArgs)"/> method.</typeparam>
    public class EffectApplyArgs<T> : EffectApplyArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectApplyArgs{T}"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="value">The value used to apply the effect.</param>
        /// <param name="type">The rendering type.</param>
        public EffectApplyArgs(Frame frame, T value, ApplyType type = ApplyType.Edit)
            : base(frame, type)
        {
            Value = value;
        }

        /// <summary>
        /// Gets or sets the value used to apply the effect.
        /// </summary>
        public T Value { get; set; }
    }
}