// PluginException.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents an error that occurred while loading a plugin, etc.
    /// </summary>
    [Serializable]
    public class PluginException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginException"/> class.
        /// </summary>
        public PluginException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public PluginException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="inner">The exception that is the cause of the current exception, or a null reference(Nothing in Visual Basic) if no inner exception is specified.</param>
        public PluginException(string? message, Exception? inner)
            : base(message, inner)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The System.Runtime.Serialization.SerializationInfo that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The System.Runtime.Serialization.StreamingContext that contains contextual information about the source or destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="info"/> is null.</exception>
        /// <exception cref="System.Runtime.Serialization.SerializationException">The class name is null or <see cref="Exception.HResult"/> is zero (0).</exception>
        protected PluginException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Gets or sets the plugin name.
        /// </summary>
        public string PluginName { get; set; } = string.Empty;
    }
}