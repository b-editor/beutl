#pragma warning disable CA2016
#pragma warning disable CA1507
#pragma warning disable IDE0008
#pragma warning disable IDE0001

using System.Text.Json.Serialization;

namespace Beutl.Api;

public class CreateVirtualAssetRequest
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("content_url")]
    public string ContentType { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("sha384")]
    public string? Sha384 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

public partial class AssetsClient
{
    /// <exception cref="BeutlApiException">A server side error occurred.</exception>
    public virtual Task<AssetMetadataResponse> PostAsync(string owner, string name, MultipartFormDataContent request)
    {
        return PostAsync(owner, name, request, CancellationToken.None);
    }

    /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
    /// <exception cref="BeutlApiException">A server side error occurred.</exception>
    public virtual async Task<AssetMetadataResponse> PostAsync(string owner, string name, MultipartFormDataContent request, CancellationToken cancellationToken)
    {
        if (request == null)
            throw new System.ArgumentNullException("request");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/api/v1/assets/{owner}/{name}");
        urlBuilder_.Replace("{owner}", Uri.EscapeDataString(ConvertToString(owner, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{name}", Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new HttpRequestMessage())
            {
                request_.Content = request;
                request_.Method = new HttpMethod("POST");

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new Uri(url_, UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<AssetMetadataResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    if (status_ == 403)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    if (status_ == 415)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new BeutlApiException("A server side error occurred.", status_, responseText_, headers_, null);
                    }
                    else
                    if (status_ == 400)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    if (status_ == 409)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new BeutlApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

    /// <exception cref="BeutlApiException">A server side error occurred.</exception>
    public virtual Task<AssetMetadataResponse> PostAsync(string owner, string name, CreateVirtualAssetRequest request)
    {
        return PostAsync(owner, name, request, CancellationToken.None);
    }

    /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
    /// <exception cref="BeutlApiException">A server side error occurred.</exception>
    public virtual async Task<AssetMetadataResponse> PostAsync(string owner, string name, CreateVirtualAssetRequest request, CancellationToken cancellationToken)
    {
        if (request == null)
            throw new System.ArgumentNullException("request");

        var urlBuilder_ = new System.Text.StringBuilder();
        urlBuilder_.Append(BaseUrl != null ? BaseUrl.TrimEnd('/') : "").Append("/api/v1/assets/{owner}/{name}");
        urlBuilder_.Replace("{owner}", Uri.EscapeDataString(ConvertToString(owner, System.Globalization.CultureInfo.InvariantCulture)));
        urlBuilder_.Replace("{name}", Uri.EscapeDataString(ConvertToString(name, System.Globalization.CultureInfo.InvariantCulture)));

        var client_ = _httpClient;
        var disposeClient_ = false;
        try
        {
            using (var request_ = new HttpRequestMessage())
            {
                var content_ = new StringContent(System.Text.Json.JsonSerializer.Serialize(request, _settings.Value));
                content_.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
                request_.Content = content_;
                request_.Method = new HttpMethod("POST");
                request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new Uri(url_, UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try
                {
                    var headers_ = Enumerable.ToDictionary(response_.Headers, h_ => h_.Key, h_ => h_.Value);
                    if (response_.Content != null && response_.Content.Headers != null)
                    {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int)response_.StatusCode;
                    if (status_ == 200)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<AssetMetadataResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        return objectResponse_.Object;
                    }
                    else
                    if (status_ == 401)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    if (status_ == 403)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    if (status_ == 415)
                    {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new BeutlApiException("A server side error occurred.", status_, responseText_, headers_, null);
                    }
                    else
                    if (status_ == 400)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    if (status_ == 409)
                    {
                        var objectResponse_ = await ReadObjectResponseAsync<ApiErrorResponse>(response_, headers_, cancellationToken).ConfigureAwait(false);
                        if (objectResponse_.Object == null)
                        {
                            throw new BeutlApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                        }
                        throw new BeutlApiException<ApiErrorResponse>("A server side error occurred.", status_, objectResponse_.Text, headers_, objectResponse_.Object, null);
                    }
                    else
                    {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new BeutlApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                    }
                }
                finally
                {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        }
        finally
        {
            if (disposeClient_)
                client_.Dispose();
        }
    }

}
#pragma warning restore IDE0001
#pragma warning restore IDE0008
#pragma warning restore CA1507
#pragma warning restore CA2016
