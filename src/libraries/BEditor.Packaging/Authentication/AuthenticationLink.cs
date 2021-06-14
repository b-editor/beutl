// AuthenticationLink.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the result of authentication.
    /// </summary>
    public class AuthenticationLink : Authentication
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationLink"/> class.
        /// </summary>
        /// <param name="auth">The <see cref="Authentication"/>.</param>
        /// <param name="provider">The <see cref="IAuthenticationProvider"/>.</param>
        public AuthenticationLink(Authentication auth, IAuthenticationProvider provider)
        {
            AuthProvider = provider;
            CopyPropertiesLocally(provider, auth);
        }

        /// <summary>
        /// Gets the <see cref="IAuthenticationProvider"/>.
        /// </summary>
        public IAuthenticationProvider AuthProvider { get; private set; }

        /// <summary>
        /// Change a password from an user with his token.
        /// </summary>
        /// <param name="password">The new password.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask ChangeUserPasswordAsync(string password)
        {
            var auth = await AuthProvider.ChangeUserEmailAsync(Token, password);

            CopyPropertiesLocally(auth.AuthProvider, auth);
        }

        /// <summary>
        /// Change a email from an user with his token.
        /// </summary>
        /// <param name="newEmail">The new email.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask ChangeUserEmailAsync(string newEmail)
        {
            var auth = await AuthProvider.ChangeUserEmailAsync(Token, newEmail);

            CopyPropertiesLocally(auth.AuthProvider, auth);
        }

        /// <summary>
        /// Updates profile (displayName) of user tied to given user token.
        /// </summary>
        /// <param name="displayName">The new display name.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask UpdateProfileAsync(string displayName)
        {
            var auth = await AuthProvider.UpdateProfileAsync(Token, displayName);

            CopyPropertiesLocally(auth.AuthProvider, auth);
        }

        /// <summary>
        /// Refresh the user details.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask RefreshUserDetailsAsync()
        {
            if (!string.IsNullOrEmpty(Token))
            {
                User = await AuthProvider.GetUserAsync(Token);
            }
        }

        /// <summary>
        /// Refresh the <see cref="AuthenticationLink"/>.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public async ValueTask RefreshAuthAsync()
        {
            var auth = await AuthProvider.RefreshAuthAsync(this);

            CopyPropertiesLocally(auth.AuthProvider, auth);
        }

        private void CopyPropertiesLocally(IAuthenticationProvider provider, Authentication auth)
        {
            AuthProvider = provider;

            if (auth != null)
            {
                Token = auth.Token;
                RefreshToken = auth.RefreshToken;
                ExpiresIn = auth.ExpiresIn;
                Created = auth.Created;
                User = auth.User;
            }
        }
    }
}