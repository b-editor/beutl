using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents a plugin config.
    /// </summary>
    public class PluginConfig
    {
        /// <summary>
        /// Iniitializes a new instance of the <see cref="PluginConfig"/> class.
        /// </summary>
        public PluginConfig(IServiceProvider service)
        {
            ServiceProvider = service;
        }


        /// <summary>
        /// Gets the ServiceProvider.
        /// </summary>
        public IServiceProvider ServiceProvider { get; }
    }
}
