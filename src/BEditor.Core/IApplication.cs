using System;
using System.Collections.Generic;

using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        /// Gets the <see cref="IServiceCollection"/>.
        /// </summary>
        public IServiceCollection Services { get; }
        /// <summary>
        /// Gets the <see cref="ILoggerFactory"/>.
        /// </summary>
        public ILoggerFactory LoggingFactory { get; }
    }
}
