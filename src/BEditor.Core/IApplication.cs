using System;
using System.Collections.Generic;

using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor
{
    /// <summary>
    /// Represents an application.
    /// </summary>
    public interface IApplication
    {
        /// <summary>
        /// Get or set the status of an application.
        /// </summary>
        public Status AppStatus { get; set; }
        /// <summary>
        /// Gets the ServiceCollection.
        /// </summary>
        public IServiceCollection Services { get; }
    }
}
