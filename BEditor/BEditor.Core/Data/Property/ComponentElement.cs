using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Property
{
    [DataContract(Namespace = "")]
    public class ComponentElement<T> : PropertyElement<T> where T : PropertyElementMetadata
    {

    }
}
