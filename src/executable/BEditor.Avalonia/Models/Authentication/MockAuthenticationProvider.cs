using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor.Models.Authentication
{
    public class MockPackageUploader : IPackageUploader
    {
        private readonly HttpClient _client;

        public MockPackageUploader(HttpClient client)
        {
            _client = client;
        }

        public async ValueTask<PackageUploadResult> UploadAsync(User user, Stream stream)
        {
            await Task.Delay(3000);
            return new(true, string.Empty);
        }
    }

    public class MockAuthenticationProvider : IAuthenticationProvider
    {
        public async ValueTask<(AuthenticationResponse Response, User? User)> SigninAsync(string email, string password)
        {
            var response = new AuthenticationResponse(true, string.Empty);
            var user = new User(string.Empty);
            await Task.Delay(3000);
            return (Response: response, User: user);
        }

        public async ValueTask<(AuthenticationResponse Response, User? User)> SignupAsync(string email, string password)
        {
            var response = new AuthenticationResponse(true, string.Empty);
            var user = new User(string.Empty);
            await Task.Delay(3000);
            return (Response: response, User: user);
        }
    }
}