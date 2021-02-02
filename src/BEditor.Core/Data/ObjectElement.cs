using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Properties;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a base class of the object.
    /// </summary>
    [DataContract]
    public abstract class ObjectElement : EffectElement
    {
        /// <summary>
        /// Filter a effect.
        /// </summary>
        public virtual bool EffectFilter(EffectElement effect) => true;
    }
}
