// Authentication.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Text.Json.Serialization;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the result of authentication.
    /// </summary>
    public class Authentication
    {
        /// <summary>
        /// Gets or sets the token which can be used for authenticated queries.
        /// </summary>
        [JsonPropertyName("id_token")]
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the refresh token of the underlying service which can be used to get a new access token.
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        // 何秒間使えるか

        /// <summary>
        /// Gets or sets the numbers of seconds since <see cref="Created"/> when the token expires.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Gets or sets when this token was created.
        /// </summary>
        public DateTime Created { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets the user.
        /// </summary>
        public User? User { get; set; }

        /// <summary>
        /// Specifies whether the token already expired.
        /// </summary>
        /// <returns>Returns <see langword="true"/> if the token has expired, <see langword="false"/> otherwise.</returns>
        public bool IsExpired()
        {
            return DateTime.Now > Created.AddSeconds(ExpiresIn - 10);
        }
    }
}