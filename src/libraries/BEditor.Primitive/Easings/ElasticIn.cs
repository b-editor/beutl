// ElasticIn.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;

using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Media;

namespace BEditor.Primitive.Easings
{
    /// <summary>
    /// To be addded.
    /// </summary>
    public sealed class ElasticIn : EasingFunc
    {
        /// <inheritdoc/>
        public override float EaseFunc(Frame frame, Frame totalframe, float min, float max)
        {
            return Funcs.ElasticIn(frame, totalframe, min, max);
        }

        /// <inheritdoc/>
        public override IEnumerable<IEasingProperty> GetProperties()
        {
            yield break;
        }
    }
}