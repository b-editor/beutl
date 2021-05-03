using System;
using System.Runtime.Serialization;

using BEditor.Media.Helpers;

namespace BEditor.Media
{
    /// <summary>
    /// Represents an exception thrown when FFMpeg method call returns an error code.
    /// </summary>
    [Serializable]
    public class FFmpegException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FFmpegException"/> class.
        /// </summary>
        public FFmpegException()
        {
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FFmpegException"/> class using a message and a error code.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public FFmpegException(string message) : base(message)
        {
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FFmpegException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public FFmpegException(string? message, Exception? innerException) : base(message, innerException)
        {
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FFmpegException"/> class using a message and a error code.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="errorCode">The error code returned by the FFmpeg method.</param>
        public FFmpegException(string message, int errorCode)
            : base($"{message} Error code: {errorCode} : {StringConverter.DecodeMessage(errorCode)}")
        {
            ErrorCode = errorCode;
            ErrorMessage = StringConverter.DecodeMessage(errorCode);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FFmpegException"/> class with serialized data.
        /// </summary>
        /// <param name="serializationInfo">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="streamingContext">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected FFmpegException(SerializationInfo serializationInfo, StreamingContext streamingContext) : base(serializationInfo, streamingContext)
        {
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Gets the error code returned by the FFmpeg method.
        /// </summary>
        public int? ErrorCode { get; }

        /// <summary>
        /// Gets the message text decoded from error code.
        /// </summary>
        public string ErrorMessage { get; }
    }
}