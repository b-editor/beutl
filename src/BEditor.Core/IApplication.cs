using System;
using System.Collections.Generic;

using BEditor.Core.Plugin;
using BEditor.Core.Service;

namespace BEditor.Core
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
    }
}
