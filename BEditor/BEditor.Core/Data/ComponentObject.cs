using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.Serialization;

namespace BEditor.Core.Data {
    [DataContract(Namespace = "")]
    public class ComponentObject : BasePropertyChanged, IExtensibleDataObject {
        private Dictionary<string, dynamic> componentData;

        public Dictionary<string, dynamic> ComponentData => componentData ??= new Dictionary<string, dynamic>();

        public virtual ExtensionDataObject ExtensionData { get; set; }
    }
}
