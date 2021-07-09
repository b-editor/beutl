// ServicesLocator.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor
{
    /// <summary>
    /// Locate services.
    /// </summary>
    public class ServicesLocator
    {
        private ILogger? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServicesLocator"/> class.
        /// </summary>
        /// <param name="provider">The service provider.</param>
        public ServicesLocator(IServiceProvider provider)
        {
            Provider = provider;
        }

        /// <summary>
        /// Gets or sets the current instance.
        /// </summary>
        [AllowNull]
        public static ServicesLocator Current { get; set; }

        /// <summary>
        /// Gets the service provider.
        /// </summary>
        public IServiceProvider Provider { get; }

        /// <summary>
        /// Gets the logger.
        /// </summary>
        public ILogger Logger => _logger ??= Provider.GetRequiredService<ILogger>();

        /// <summary>
        /// Gets the settings folder.
        /// </summary>
        /// <returns>Returns the directory path.</returns>
        public static string GetUserFolder()
        {
            if (OperatingSystem.IsWindows())
            {
                return CreateDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BEditor"));
            }
            else
            {
                return CreateDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "BEditor"));
            }
        }

        /// <summary>
        /// Gets the plugins folder.
        /// </summary>
        /// <returns>Returns the directory path.</returns>
        public static string GetPluginsFolder()
        {
            if (OperatingSystem.IsWindows())
            {
                return CreateDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BEditor", "plugins"));
            }
            else
            {
                return CreateDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".beditor", "plugins"));
            }
        }

        private static string CreateDir(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return dir;
        }
    }
}