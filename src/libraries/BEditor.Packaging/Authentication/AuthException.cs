// AuthException.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents an error thrown in the <see cref="IAuthenticationProvider"/>.
    /// </summary>
    [Serializable]
    public class AuthException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuthException"/> class.
        /// </summary>
        public AuthException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public AuthException(string? message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="inner">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public AuthException(string? message, Exception? inner)
            : base(message, inner)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthException"/> class with serialized data.
        /// </summary>
        /// <param name="info">The <see cref="System.Runtime.Serialization.SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="System.Runtime.Serialization.StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AuthException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Gets or sets the post data passed to the authentication service.
        /// </summary>
        public string RequestData { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the request url.
        /// </summary>
        public string RequestUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the response from the authentication service.
        /// </summary>
        public string ResponseData { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the http status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }
    }
}