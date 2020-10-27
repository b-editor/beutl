using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.Serialization;

namespace BEditorCore.Data {
    [DataContract(Namespace = "")]
    public class ComponentObject : BasePropertyChanged, IExtensibleDataObject {
        private dynamic componentData;

        public dynamic ComponentData => componentData ??= new ExpandoObject();

        public virtual ExtensionDataObject ExtensionData { get; set; }

        public bool Contains(string key) => ((IDictionary<string, object>)ComponentData).ContainsKey(key);
    }
}
