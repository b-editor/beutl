using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor.Models.Authentication
{
    public class AuthenticationProvider : IAuthenticationProvider
    {
        private const string Base = "https://api.beditor.net";
        private const string Refresh = "/api/refreshauth?key={0}";
        private const string SignUp = "/api/signup?key={0}";
        private const string SignIn = "/api/signin?key={0}";
        private const string GetAccountInfo = "/api/getAccountInfo?key={0}";
        private const string Update = "/api/update?key={0}";
        private const string DeleteAccount = "/api/deleteAccount?key={0}";
        private const string SendPasswordResetEmail = "/api/sendPasswordResetEmail?key={0}";
        private readonly HttpClient _client;

        public AuthenticationProvider(HttpClient client)
        {
            _client = client;
        }

        public ValueTask<AuthenticationLink> ChangeUserEmailAsync(string token, string newEmail)
        {
            var url = Combine(Update);
            var requestData = $"{{\"access_token\": \"{token}\",\"email\": \"{newEmail}\"}}";

            return ExecuteWithPostContentAsync(url, requestData);
        }

        public ValueTask<AuthenticationLink> ChangeUserPasswordAsync(string token, string password)
        {
            var url = Combine(Update);
            var requestData = $"{{\"access_token\": \"{token}\",\"password\": \"{password}\"}}";

            return ExecuteWithPostContentAsync(url, requestData);
        }

        public ValueTask<AuthenticationLink> CreateUserAsync(string email, string password, string displayName = "")
        {
            var url = Combine(SignUp);
            var requestData = $"{{\"email\": \"{email}\",\"password\": \"{password}\",\"displayname\":{displayName}}}";

            return ExecuteWithPostContentAsync(url, requestData);
        }

        public async ValueTask DeleteUserAsync(string token)
        {
            var url = Combine(DeleteAccount);
            var requestData = $"{{\"access_token\":\"{token}\"}}";

            try
            {
                var msg = await _client.PostAsync(url, new StringContent(requestData));
                msg.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new AuthException(null, ex)
                {
                    RequestUrl = url,
                    RequestData = requestData,
                };
            }
        }

        public async ValueTask<User> GetUserAsync(string token)
        {
            var url = Combine(GetAccountInfo);
            var responseData = "N/A";
            var requestData = $"{{\"access_token\":\"{token}\"}}";

            try
            {
                var msg = await _client.PostAsync(url, new StringContent(requestData));
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
                var user = JsonSerializer.Deserialize<Packaging.User>(responseData);

                if (user is null)
                {
                    throw new JsonException("Failed to deserialize.");
                }

                return user;
            }
            catch (Exception ex)
            {
                throw new AuthException(null, ex)
                {
                    RequestUrl = url,
                    RequestData = requestData,
                    ResponseData = responseData,
                };
            }
        }

        public ValueTask<AuthenticationLink> RefreshAuthAsync(AuthenticationLink auth)
        {
            var url = Combine(Refresh);
            var requestData = $"{{\"type\":\"refresh_token\",\"token\": \"{auth.RefreshToken}\"}}";

            return ExecuteWithPostContentAsync(url, requestData);
        }

        public async ValueTask SendPasswordResetEmailAsync(string token, string email)
        {
            var url = Combine(SendPasswordResetEmail);
            var requestData = $"{{\"email\":\"{email}\"}}";

            try
            {
                var msg = await _client.PostAsync(url, new StringContent(requestData));
                msg.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new AuthException(null, ex)
                {
                    RequestUrl = url,
                    RequestData = requestData,
                };
            }
        }

        public ValueTask<AuthenticationLink> SignInAsync(string email, string password)
        {
            var url = Combine(SignIn);
            var requestData = $"{{\"email\": \"{email}\",\"password\": \"{password}\"}}";

            return ExecuteWithPostContentAsync(url, requestData);
        }

        public ValueTask<AuthenticationLink> UpdateProfileAsync(string token, string displayName)
        {
            var url = Combine(Update);
            var requestData = $"{{\"access_token\": \"{token}\",\"displayname\": \"{displayName}\"}}";

            return ExecuteWithPostContentAsync(url, requestData);
        }

        private async ValueTask<AuthenticationLink> ExecuteWithPostContentAsync(string url, string requestData)
        {
            var responseData = "N/A";

            try
            {
                var msg = await _client.PostAsync(url, new StringContent(requestData));
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
                var auth = JsonSerializer.Deserialize<Packaging.Authentication>(responseData);

                if (auth is null)
                {
                    throw new JsonException("Failed to deserialize.");
                }

                return new AuthenticationLink(auth, this);
            }
            catch (Exception ex)
            {
                throw new AuthException(null, ex)
                {
                    RequestUrl = url,
                    RequestData = requestData,
                    ResponseData = responseData,
                };
            }
        }

        private static string Combine(string url)
        {
            // Todo: APIキーを追加
            return Base + string.Format(url, "");
        }
    }
}