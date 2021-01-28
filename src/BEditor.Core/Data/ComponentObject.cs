using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.Serialization;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the base class of the edit data.
    /// </summary>
    [DataContract]
    public class ComponentObject : BasePropertyChanged, IExtensibleDataObject
    {
        private Dictionary<string, dynamic>? _ComponentData;

        /// <summary>
        /// Get a Dictionary to put the cache in.
        /// </summary>
        public Dictionary<string, dynamic> ComponentData => _ComponentData ??= new Dictionary<string, dynamic>();
        /// <inheritdoc/>
        public virtual ExtensionDataObject? ExtensionData { get; set; }
    }
}
