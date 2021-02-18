using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Threading;

using BEditor.Data.Property;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the base class of the edit data.
    /// </summary>
    [DataContract]
    public class EditorObject : BasePropertyChanged, IExtensibleDataObject
    {
        private Dictionary<string, dynamic>? _ComponentData;

        /// <summary>
        /// Gets the synchronization context for this object.
        /// </summary>
        public SynchronizationContext? Synchronize { get; private set; } = SynchronizationContext.Current;
        /// <summary>
        /// Get a Dictionary to put the cache in.
        /// </summary>
        public Dictionary<string, dynamic> ComponentData => _ComponentData ??= new Dictionary<string, dynamic>();
        /// <inheritdoc/>
        public virtual ExtensionDataObject? ExtensionData
        {
            get => null;
            set => Synchronize = SynchronizationContext.Current;
        }
        /// <summary>
        /// Gets the ServiceProvider.
        /// </summary>
        public ServiceProvider? ServiceProvider { get; internal set; }
    }
}
