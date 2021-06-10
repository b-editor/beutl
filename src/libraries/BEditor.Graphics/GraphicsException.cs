// GraphicsException.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.Serialization;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an exception that is raised when an error occurs using OpenGL.
    /// </summary>
    [Serializable]
    public class GraphicsException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsException"/> class.
        /// </summary>
        public GraphicsException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public GraphicsException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="inner">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public GraphicsException(string message, Exception inner)
            : base(message, inner)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
        /// <exception cref="SerializationException">The class name is <see langword="null"/> or <see cref="Exception.HResult"/> is zero (0).</exception>
        protected GraphicsException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}