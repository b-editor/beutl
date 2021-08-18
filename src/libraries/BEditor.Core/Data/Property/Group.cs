// Group.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Property.Easing;
using BEditor.Media;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a base class for grouping <see cref="PropertyElement"/>.
    /// </summary>
    public abstract class Group : PropertyElement, IKeyframeProperty, IEasingProperty, IParent<PropertyElement>
    {
        /// <inheritdoc/>
        event Action<PositionInfo>? IKeyframeProperty.Added
        {
            add
            {
            }
            remove
            {
            }
        }

        /// <inheritdoc/>
        event Action<PositionInfo>? IKeyframeProperty.Removed
        {
            add
            {
            }

            remove
            {
            }
        }

        /// <inheritdoc/>
        event Action<int, int>? IKeyframeProperty.Moved
        {
            add
            {
            }

            remove
            {
            }
        }

        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => GetProperties();

        /// <inheritdoc/>
        public override EffectElement Parent
        {
            get => base.Parent;
            set
            {
                base.Parent = value;

                if (Children != null)
                {
                    foreach (var item in Children)
                    {
                        if (item is not null)
                            item.Parent = Parent;
                    }
                }
            }
        }

        /// <inheritdoc/>
        EasingFunc? IKeyframeProperty.EasingType => null;

        /// <summary>
        /// Gets the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        /// <returns>Returns the <see cref="PropertyElement"/> to display on the GUI.</returns>
        public abstract IEnumerable<PropertyElement> GetProperties();

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.AddFrame(PositionInfo frame)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        IEnumerable<PositionInfo> IKeyframeProperty.Enumerate()
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        int IKeyframeProperty.IndexOf(PositionInfo position)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.MoveFrame(int fromIndex, PositionInfo toFrame)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.RemoveFrame(PositionInfo frame)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        IRecordCommand IKeyframeProperty.UpdatePositionInfo(int index, PositionInfo position)
        {
            throw new NotSupportedException();
        }
    }
}