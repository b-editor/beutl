using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor.Models.Authentication
{
    public class PackageUploader : IRemotePackageProvider
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
            var requestData = $"{{\"access_token\":\"{auth.Token}\"}}";

            try
            {
                var msg = await _client.PostAsync(url, new StringContent(requestData));
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
                    RequestData = requestData,
                    ResponseData = responseData,
                };
            }
        }

        public async ValueTask UploadAsync(Packaging.Authentication user, Stream stream)
        {
            // Todo: APIキーを追加
            var url = Base + string.Format(Upload, "", user.Token);
            var responseData = "N/A";

            try
            {
                var msg = await _client.PostAsync(url, new StreamContent(stream));
                msg.EnsureSuccessStatusCode();
                responseData = await msg.Content.ReadAsStringAsync();
                // var packages = JsonSerializer.Deserialize<PackageVersion>(responseData);

                // if (packages is null)
                // {
                //     throw new JsonException("Failed to deserialize.");
                // }

                // return packages;
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

        private static string Combine(string url)
        {
            // Todo: APIキーを追加
            return Base + string.Format(url, "");
        }
    }
}