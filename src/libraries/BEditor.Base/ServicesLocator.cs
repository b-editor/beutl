// ServicesLocator.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BEditor
{
    /// <summary>
    /// Locate services.
    /// </summary>
    public class ServicesLocator
    {
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
    }
}
