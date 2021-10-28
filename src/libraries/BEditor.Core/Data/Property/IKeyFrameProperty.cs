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

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property that has an editing window on the timeline.
    /// </summary>
    public interface IKeyframeProperty : IPropertyElement, IParentSingle<EasingFunc?>
    {
        /// <summary>
        /// Occurs when a keyframe is added.
        /// <para>arg1: The added position.</para>
        /// </summary>
        public event Action<PositionInfo>? Added;

        /// <summary>
        /// Occurs when a keyframe is removed.
        /// <para>obj: The removed position.</para>
        /// </summary>
        public event Action<PositionInfo>? Removed;

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
        /// <param name="frame">Position to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand AddFrame(PositionInfo frame);

        /// <summary>
        /// Create a command to remove a keyframe.
        /// </summary>
        /// <param name="frame">Position to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand RemoveFrame(PositionInfo frame);

        /// <summary>
        /// Create a command to move a keyframe.
        /// </summary>
        /// <param name="fromIndex">Index of the frame to be moved from.</param>
        /// <param name="toFrame">Destination position.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand MoveFrame(int fromIndex, PositionInfo toFrame);

        /// <summary>
        /// Create a command to update the <see cref="PositionInfo"/> for the specified index.
        /// </summary>
        /// <param name="index">The index of <see cref="PositionInfo"/> to update.</param>
        /// <param name="position">The new <see cref="PositionInfo"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand UpdatePositionInfo(int index, PositionInfo position);

        /// <summary>
        /// Determines the index of a specific item in the <see cref="IKeyframeProperty"/>.
        /// </summary>
        /// <param name="position">The object to locate in the <see cref="IKeyframeProperty"/>.</param>
        /// <returns>The index of item if found in the list; otherwise, -1.</returns>
        public int IndexOf(PositionInfo position);

        /// <summary>
        /// Gets an enumerable collection of position information.
        /// </summary>
        /// <returns>An enumerable collection of position information.</returns>
        public IEnumerable<PositionInfo> Enumerate();
    }

    /// <summary>
    /// Represents a property that has an editing window on the timeline.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    public interface IKeyframeProperty<T> : IKeyframeProperty
        where T : notnull
    {
        /// <summary>
        /// Gets the <see cref="List{Frame}"/> of the frame number corresponding to value.
        /// </summary>
        public ObservableCollection<KeyFramePair<T>> Pairs { get; }
    }
}