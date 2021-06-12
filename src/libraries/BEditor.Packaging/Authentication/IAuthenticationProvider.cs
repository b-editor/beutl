// IAuthenticationProvider.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Threading.Tasks;

namespace BEditor.Packaging
{
    /// <summary>
    /// Represents the authentication provider.
    /// </summary>
    public interface IAuthenticationProvider
    {
        /// <summary>
        /// Refresh the <see cref="AuthenticationLink"/>.
        /// </summary>
        /// <param name="auth">The <see cref="AuthenticationLink"/>.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask<AuthenticationLink> RefreshAuthAsync(AuthenticationLink auth);

        /// <summary>
        /// Using the provided email and password, get the firebase auth with token and basic user credentials.
        /// </summary>
        /// <param name="email">The email.</param>
        /// <param name="password">The password.</param>
        /// <returns>Returns the <see cref="AuthenticationLink"/>.</returns>
        public ValueTask<AuthenticationLink> SignInAsync(string email, string password);

        /// <summary>
        /// Creates new user with given credentials.
        /// </summary>
        /// <param name="email">The email.</param>
        /// <param name="password">The password.</param>
        /// <param name="displayName">Optional display name.</param>
        /// <returns>Returns the <see cref="AuthenticationLink"/>.</returns>
        public ValueTask<AuthenticationLink> CreateUserAsync(string email, string password, string displayName = "");

        /// <summary>
        /// Using the idToken of an authenticated user, get the details of the user's account.
        /// </summary>
        /// <param name="token">The token (idToken) of an authenticated user.</param>
        /// <returns>Returns the <see cref="User"/>.</returns>
        public ValueTask<User> GetUserAsync(string token);

        /// <summary>
        /// Change a password from an user with his token.
        /// </summary>
        /// <param name="token">The Token from an user.</param>
        /// <param name="password">The new password.</param>
        /// <returns>Returns the <see cref="AuthenticationLink"/>.</returns>
        public ValueTask<AuthenticationLink> ChangeUserPasswordAsync(string token, string password);

        /// <summary>
        /// Change a email from an user with his token.
        /// </summary>
        /// <param name="token">The Token from an user.</param>
        /// <param name="newEmail">The new email.</param>
        /// <returns>Returns the <see cref="AuthenticationLink"/>.</returns>
        public ValueTask<AuthenticationLink> ChangeUserEmailAsync(string token, string newEmail);

        /// <summary>
        /// Updates profile (displayName) of user tied to given user token.
        /// </summary>
        /// <param name="token">The Token from an user.</param>
        /// <param name="displayName">The new display name.</param>
        /// <returns>Returns the <see cref="AuthenticationLink"/>.</returns>
        public ValueTask<AuthenticationLink> UpdateProfileAsync(string token, string displayName);

        /// <summary>
        /// Deletes the user with a recent token.
        /// </summary>
        /// <param name="token">Recent Token.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask DeleteUserAsync(string token);

        /// <summary>
        /// Sends user an email with a link to reset his password.
        /// </summary>
        /// <param name="token">The Token from an user.</param>
        /// <param name="email">The email.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
        public ValueTask SendPasswordResetEmailAsync(string token, string email);
    }
}