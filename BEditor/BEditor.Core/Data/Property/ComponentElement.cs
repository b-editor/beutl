using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.Property
{
    [DataContract]
    public class ComponentElement<T> : PropertyElement<T> where T : PropertyElementMetadata
    {

    }
}
