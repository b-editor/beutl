using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor.Models.Authentication
{
    public sealed class AuthenticationProvider : IAuthenticationProvider
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
            var requestData = $"{{\"email\": \"{newEmail}\"}}";

            return ExecuteWithPostContentAsync(url, requestData, token);
        }

        public ValueTask<AuthenticationLink> ChangeUserPasswordAsync(string token, string password)
        {
            var url = Combine(Update);
            var requestData = $"{{\"password\": \"{password}\"}}";

            return ExecuteWithPostContentAsync(url, requestData, token);
        }

        public ValueTask<AuthenticationLink> CreateUserAsync(string email, string password, string displayName = "")
        {
            var url = Combine(SignUp);
            var requestData = $"{{\"email\": \"{email}\",\"password\": \"{password}\",\"displayname\":{displayName}}}";

            return ExecuteWithPostContentAsync(url, requestData, null);
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

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddToken(request.Headers, token);

                var msg = await _client.SendAsync(request);
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
                var user = JsonSerializer.Deserialize<User>(responseData);

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
                    ResponseData = responseData,
                };
            }
        }

        public async ValueTask<AuthenticationLink> RefreshAuthAsync(AuthenticationLink auth)
        {
            var responseData = "N/A";
            var url = Combine(Refresh);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                AddToken(request.Headers, auth.RefreshToken);

                var msg = await _client.SendAsync(request);
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
                var auth2 = JsonSerializer.Deserialize<Packaging.Authentication>(responseData);

                if (auth2 is null)
                {
                    throw new JsonException("Failed to deserialize.");
                }

                return new AuthenticationLink(auth2, this);
            }
            catch (Exception ex)
            {
                throw new AuthException(null, ex)
                {
                    RequestUrl = url,
                    ResponseData = responseData,
                };
            }
        }

        public async ValueTask SendPasswordResetEmailAsync(string token, string email)
        {
            var url = Combine(SendPasswordResetEmail);
            var requestData = $"{{\"email\":\"{email}\"}}";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                AddToken(request.Headers, token);
                request.Content = new StringContent(requestData);

                var msg = await _client.SendAsync(request);
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

            return ExecuteWithPostContentAsync(url, requestData, null);
        }

        public ValueTask<AuthenticationLink> UpdateProfileAsync(string token, string displayName)
        {
            var url = Combine(Update);
            var requestData = $"{{\"displayname\": \"{displayName}\"}}";

            return ExecuteWithPostContentAsync(url, requestData, token);
        }

        private async ValueTask<AuthenticationLink> ExecuteWithPostContentAsync(string url, string requestData, string? token)
        {
            var responseData = "N/A";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                AddToken(request.Headers, token);
                request.Content = new StringContent(requestData);

                var msg = await _client.SendAsync(request);
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

        private static void AddToken(HttpRequestHeaders header, string? item)
        {
            if (item != null)
            {
                header.Add("Authorization", "Token " + item);
            }
        }

        private static string Combine(string url)
        {
            // Todo: APIキーを追加
            return Base + string.Format(url, "");
        }
    }
}