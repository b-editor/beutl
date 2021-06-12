using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor.Models.Authentication
{
    public class MockPackageUploader : IRemotePackageProvider
    {
        private readonly HttpClient _client;

        public MockPackageUploader(HttpClient client)
        {
            _client = client;
        }

        public async ValueTask<IEnumerable<Package>> GetPackagesAsync(Packaging.Authentication auth)
        {
            await Task.Delay(3000);
            return Enumerable.Empty<Package>();
        }

        public async ValueTask UploadAsync(Packaging.Authentication user, Stream stream)
        {
            await Task.Delay(3000);
        }
    }

    public class MockAuthenticationProvider : IAuthenticationProvider
    {
        private AuthenticationLink? _instance;

        public async ValueTask<AuthenticationLink> ChangeUserEmailAsync(string token, string newEmail)
        {
            await Task.Delay(3000);
            _instance!.User!.Email = newEmail;
            return _instance;
        }

        public async ValueTask<AuthenticationLink> ChangeUserPasswordAsync(string token, string password)
        {
            await Task.Delay(3000);
            return _instance!;
        }

        public async ValueTask<AuthenticationLink> CreateUserAsync(string email, string password, string displayName = "")
        {
            _instance = new(
                new()
                {
                    ExpiresIn = 604_800,
                    User = new()
                    {
                        Email = email,
                        DisplayName = displayName
                    }
                },
                this);

            await Task.Delay(3000);

            return _instance;
        }

        public async ValueTask DeleteUserAsync(string token)
        {
            await Task.Delay(3000);
        }

        public async ValueTask<User> GetUserAsync(string token)
        {
            _instance ??= new(
                new()
                {
                    ExpiresIn = 604_800,
                    User = new()
                },
                this);

            await Task.Delay(3000);
            return _instance.User!;
        }

        public async ValueTask<AuthenticationLink> RefreshAuthAsync(AuthenticationLink auth)
        {
            await Task.Delay(3000);
            auth.Created = DateTime.Now;
            auth.ExpiresIn = 604_800;
            auth.User = new();
            return _instance = auth;
        }

        public async ValueTask SendPasswordResetEmailAsync(string token, string email)
        {
            await Task.Delay(3000);
        }

        public async ValueTask<AuthenticationLink> SignInAsync(string email, string password)
        {
            _instance = new(
                new()
                {
                    ExpiresIn = 604_800,
                    User = new() { Email = email }
                },
                this);

            await Task.Delay(3000);

            return _instance;
        }

        public async ValueTask<AuthenticationLink> UpdateProfileAsync(string token, string displayName)
        {
            _instance!.User!.DisplayName = displayName;

            await Task.Delay(3000);

            return _instance;
        }
    }
}