using System;

namespace BEditor.Audio
{
    /// <summary>
    /// Represents an exception that is raised when an error occurs using OpenGL.
    /// </summary>
    [Serializable]
    public class AudioException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioException"/> class.
        /// </summary>
        public AudioException() { }
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public AudioException(string message) : base(message) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="inner">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public AudioException(string message, Exception inner) : base(message, inner) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="AudioException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="info"/> is <see langword="null"/>.</exception>
        /// <exception cref="SerializationException">The class name is <see langword="null"/> or <see cref="Exception.HResult"/> is zero (0).</exception>
        protected AudioException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}