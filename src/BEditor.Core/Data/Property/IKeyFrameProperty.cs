using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Data.Property.Easing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property that has an editing window on the timeline.
    /// </summary>
    public interface IKeyFrameProperty : IChild<EffectElement>, IPropertyElement
    {
        /// <summary>
        /// Get or set the current <see cref="EasingFunc"/>.
        /// </summary>
        public EasingFunc? EasingType { get; }
    }
}
