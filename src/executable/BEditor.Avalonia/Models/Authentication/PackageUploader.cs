using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor.Models.Authentication
{
    public sealed class PackageUploader : IRemotePackageProvider
    {
        private const string Base = "https://api.beditor.net";
        private const string GetPackages = "/api/getPackages?key={0}";
        private const string Upload = "/api/upload?key={0}";
        private readonly HttpClient _client;

        public PackageUploader(HttpClient client)
        {
            _client = client;
        }

        public async ValueTask<IEnumerable<Package>> GetPackagesAsync(Packaging.Authentication auth)
        {
            var url = Combine(GetPackages);
            var responseData = "N/A";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                AddToken(request.Headers, auth.Token);

                var msg = await _client.SendAsync(request);
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
                var packages = JsonSerializer.Deserialize<IEnumerable<Package>>(responseData);

                if (packages is null)
                {
                    throw new JsonException("Failed to deserialize.");
                }

                return packages;
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

        public async ValueTask UploadAsync(Packaging.Authentication user, Stream stream)
        {
            // Todo: APIキーを追加
            var url = Base + string.Format(Upload, "");
            var responseData = "N/A";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                AddToken(request.Headers, user.Token);
                request.Content = new StreamContent(stream);

                var msg = await _client.SendAsync(request);
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
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