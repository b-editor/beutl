using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Properties;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the base class of the object.
    /// </summary>
    [DataContract]
    public abstract class ObjectElement : EffectElement
    {
        public virtual bool EffectFilter(EffectElement effect) => true;
    }
}
