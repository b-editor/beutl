using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.Property.Easing;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a property that can be used by <see cref="EasingFunc"/>
    /// </summary>
    public interface IEasingProperty : IPropertyElement
    {
        /// <inheritdoc cref="IChild{T}.Parent"/>
        public new EffectElement? Parent { get; set; }
    }
}
