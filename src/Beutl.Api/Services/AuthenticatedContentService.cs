namespace Beutl.Api.Services;

internal sealed class AuthenticatedContentService(BeutlApiApplication application)
    : IAuthenticatedContentService
{
    public async Task<AiContentDownload> CopyToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentUri);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        Uri downloadUri = ValidateContentUri(contentUri);
        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        AuthenticatedApiResult<AiContentDownload> result =
            await application.SendAuthenticatedAsync(
            async (authorization, requestToken) =>
            {
                using HttpRequestMessage request = new(HttpMethod.Get, downloadUri);
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                using HttpResponseMessage response =
                    await application.HttpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken);
                if (!response.IsSuccessStatusCode)
                {
                    // Said as its own failure rather than as a failed request:
                    // by the time a result is fetched the job has run and been
                    // charged for, and the caller has somewhere to send the
                    // user for it.
                    throw new AiContentUnavailableException((int)response.StatusCode);
                }
                string? fileName = NormalizeContentDispositionFileName(
                    response.Content.Headers.ContentDisposition);
                string? contentType = response.Content.Headers.ContentType?.MediaType;
                AiContentMetadata? metadata = string.IsNullOrWhiteSpace(fileName)
                    && string.IsNullOrWhiteSpace(contentType)
                        ? null
                        : new AiContentMetadata(fileName, contentType);
                await using Stream source = await response.Content.ReadAsStreamAsync(requestToken);
                await source.CopyToAsync(destination, requestToken);
                return new AiContentDownload(metadata);
            },
            operationCts.Token);
        return result.Value;
    }

    private Uri ValidateContentUri(Uri contentUri)
    {
        Uri? baseAddress = application.HttpClient.BaseAddress;
        if (!contentUri.IsAbsoluteUri
            || baseAddress is null
            || !string.Equals(contentUri.Scheme, baseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(contentUri.IdnHost, baseAddress.IdnHost, StringComparison.OrdinalIgnoreCase)
            || contentUri.Port != baseAddress.Port
            || !contentUri.AbsolutePath.StartsWith("/api/contents/", StringComparison.Ordinal)
            || contentUri.AbsolutePath.Length == "/api/contents/".Length)
        {
            throw new ArgumentException(
                "The URI must identify Beutl content on the configured API origin.",
                nameof(contentUri));
        }

        return contentUri;
    }

    private static string? NormalizeContentDispositionFileName(
        System.Net.Http.Headers.ContentDispositionHeaderValue? disposition)
    {
        if (disposition is null)
            return null;

        string? encoded = disposition.FileNameStar;
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            try
            {
                string value = encoded.Trim().Trim('"');
                int charsetEnd = value.IndexOf('\'');
                int languageEnd = charsetEnd < 0 ? -1 : value.IndexOf('\'', charsetEnd + 1);
                if (charsetEnd > 0 && languageEnd > charsetEnd)
                {
                    string charset = value[..charsetEnd];
                    if (!charset.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new AiException(
                            "The AI response filename uses an unsupported character encoding.");
                    }

                    value = value[(languageEnd + 1)..];
                }

                encoded = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException ex)
            {
                throw new AiException("The AI response contains an invalid content filename.", ex);
            }
        }

        return (encoded ?? disposition.FileName)?.Trim().Trim('"');
    }
}
