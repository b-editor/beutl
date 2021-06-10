// IKeyFrameProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using BEditor.Command;
using BEditor.Data.Property.Easing;
using BEditor.Media;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property that has an editing window on the timeline.
    /// </summary>
    public interface IKeyframeProperty : IPropertyElement, IParentSingle<EasingFunc?>
    {
        /// <summary>
        /// Occurs when a keyframe is added.
        /// <para>arg1: The added frame, arg2: The Index of the values.</para>
        /// </summary>
        public event Action<float, int>? Added;

        /// <summary>
        /// Occurs when a keyframe is removed.
        /// <para>obj: The Index of the values.</para>
        /// </summary>
        public event Action<int>? Removed;

        /// <summary>
        /// Occurs when a keyframe is moved.
        /// <para>arg1: The index of the source Frames., arg2: The index of the destination Frames.</para>
        /// </summary>
        public event Action<int, int>? Moved;

        /// <summary>
        /// Gets the current <see cref="EasingFunc"/>.
        /// </summary>
        public EasingFunc? EasingType { get; }

        /// <inheritdoc/>
        EasingFunc? IParentSingle<EasingFunc?>.Child => EasingType;

        /// <summary>
        /// Create a command to add a keyframe.
        /// </summary>
        /// <param name="frame">Frame to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand AddFrame(float frame);

        /// <summary>
        /// Create a command to remove a keyframe.
        /// </summary>
        /// <param name="frame">Frame to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand RemoveFrame(float frame);

        /// <summary>
        /// Create a command to move a keyframe.
        /// </summary>
        /// <param name="fromIndex">Index of the frame to be moved from.</param>
        /// <param name="toFrame">Destination frame.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand MoveFrame(int fromIndex, float toFrame);
    }

    /// <summary>
    /// Represents a property that has an editing window on the timeline.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    public interface IKeyframeProperty<T> : IKeyframeProperty
    {
        /// <summary>
        /// Gets the <see cref="List{Frame}"/> of the frame number corresponding to value.
        /// </summary>
        public ObservableCollection<KeyValuePair<float, T>> Pairs { get; }
    }
}