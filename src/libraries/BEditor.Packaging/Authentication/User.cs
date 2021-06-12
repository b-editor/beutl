// User.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BEditor.Packaging
{
#pragma warning disable CS1591, SA1201, SA1600
    public record AuthenticationResponse(bool Complete, string Message);

    public record PackageUploadResult(bool Complete, string Message);

    public interface IPackageUploader
    {
        public async ValueTask<PackageUploadResult> UploadAsync(User user, string filename)
        {
            if (!File.Exists(filename)) throw new FileNotFoundException(null, filename);
            await using var stream = new FileStream(filename, FileMode.Open);
            return await UploadAsync(user, stream);
        }

        public ValueTask<PackageUploadResult> UploadAsync(User user, Stream stream);
    }

    public interface IAuthenticationProvider
    {
        public ValueTask<(AuthenticationResponse Response, User? User)> SigninAsync(string email, string password);

        public ValueTask<(AuthenticationResponse Response, User? User)> SignupAsync(string email, string password);

        public ValueTask<(AuthenticationResponse Response, User? User)> GetAsync(string token);

        public ValueTask<AuthenticationResponse> UpdateAsync(User user);
    }

    public class User
    {
        /// <summary>
        /// Gets or sets the access token.
        /// </summary>
        [JsonPropertyName("token")]
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the user name.
        /// </summary>
        [JsonPropertyName("username")]
        public string UserName { get; set; } = string.Empty;

        public static async Task<User?> FromFileAsync(string filename, IAuthenticationProvider provider)
        {
            if (!File.Exists(filename)) return null;
            using var reader = new StreamReader(filename);
            var token = reader.ReadLine();

            if (token is null) return null;
            var (response, user) = await provider.GetAsync(token);
            if (response.Complete) return user;

            return user;
        }

        public void Save(string filename)
        {
            using var stream = new FileStream(filename, FileMode.Create);
            using var writer = new StreamWriter(stream);

            writer.WriteLine(AccessToken);
            writer.WriteLine(Email);
            writer.WriteLine(UserName);
        }
    }
#pragma warning restore CS1591, SA1201, SA1600
}
