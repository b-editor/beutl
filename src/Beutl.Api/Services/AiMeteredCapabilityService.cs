using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beutl.Api.Clients;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Refit;

namespace Beutl.Api.Services;

internal abstract class AiMeteredCapabilityService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
{
    internal const int MaximumResponseBodyBytes = 32 * 1024 * 1024;

    protected const string IdempotencyKeyHeader = "Idempotency-Key";

    protected BeutlApiApplication Application { get; } = application;

    protected static string CreateIdempotencyKey() => Guid.NewGuid().ToString("D");

    // Each stream joins `opened` as soon as it opens, so the caller's finally also closes the ones opened before
    // a failure.
    protected static async Task<List<StreamPart>> OpenReferencePartsAsync(
        IReadOnlyList<AiUploadSource> references,
        long maxBytesEach,
        long? maxTotalBytes,
        List<Stream> opened,
        CancellationToken cancellationToken)
    {
        var parts = new List<StreamPart>(references.Count);
        foreach (AiUploadSource reference in references)
        {
            Stream stream = await AiUploadValidation.OpenAsync(
                reference,
                maxBytesEach,
                cancellationToken);
            opened.Add(stream);
            if (maxTotalBytes is { } maxTotal
                && opened.Sum(value => value.Length - value.Position) > maxTotal)
            {
                throw new AiFileTooLargeException();
            }
            parts.Add(AiMultipartFormData.File(
                stream,
                reference.FileName,
                reference.MediaType,
                "reference[]"));
        }

        return parts;
    }

    protected static async ValueTask DisposeReferenceStreamsAsync(IReadOnlyList<Stream> streams)
    {
        List<Exception>? failures = null;
        foreach (Stream stream in streams)
        {
            try
            {
                await stream.DisposeAsync();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            // This runs from finally: cleanup must not replace a created job,
            // a definitive API rejection, cancellation, or an upload-open error.
            try
            {
                Log.CreateLogger<AiMeteredCapabilityService>().LogWarning(
                    new AggregateException("AI reference stream cleanup failed.", failures),
                    "Failed to close {Count} AI reference streams.", failures.Count);
            }
            catch
            {
                // A diagnostic sink must not replace the request outcome either.
            }
        }
    }

    /// <summary>
    /// Runs a request that answers a piece at a time, handing each piece to the
    /// caller and returning what the closing event carries.
    /// </summary>
    /// <remarks>
    /// Everything the server can refuse before it starts working still comes
    /// back as an ordinary status code, and is converted here exactly as a
    /// Refit reply would be. Once the stream is open the answer is one of two
    /// events; a stream that carries neither was cut off, and whatever it was
    /// carrying may well have finished and been charged for, which is why that
    /// is said in its own way rather than as a plain failure.
    /// </remarks>
    protected async Task<TResult> ExecuteStreamingAsync<TResult>(
        string activityName,
        Func<HttpRequestMessage> createRequest,
        Action<AiServerSentEvent> onProgress,
        Func<string, TResult> readResult,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        bool notifyJobsChanged = true,
        bool requestEventStream = true)
    {
        using CancellationTokenSource operationCts =
            Application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = Application.ActivitySource.StartActivity(
            activityName,
            ActivityKind.Client);
        configureActivity?.Invoke(activity);

        AuthenticatedApiResult<TResult> response = await Application.SendAuthenticatedAsync(
            async (authorization, requestToken) =>
            {
                using HttpRequestMessage request = createRequest();
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                if (requestEventStream)
                {
                    request.Headers.Accept.Add(
                        new MediaTypeWithQualityHeaderValue(AiEventStream.MediaType));
                }
                using HttpResponseMessage message = await Application.HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken);
                if (!message.IsSuccessStatusCode)
                {
                    throw await ToFailureAsync(message, activity, requestToken);
                }

                if (!AiEventStream.IsEventStream(message))
                {
                    // A server that does not stream answers the same request in
                    // one piece, and that answer is the same shape the closing
                    // event would have carried. There is simply nothing to show
                    // on the way.
                    return readResult(await ReadResponseBodyAsync(
                        message.Content,
                        requestToken));
                }

                await using Stream stream = await message.Content.ReadAsStreamAsync(requestToken);
                await foreach (AiServerSentEvent item in
                    AiEventStream.ReadAsync(stream, requestToken))
                {
                    switch (item.Event)
                    {
                        case AiEventStream.ResultEvent:
                            return readResult(item.Data);
                        case AiEventStream.ErrorEvent:
                            activity?.SetStatus(ActivityStatusCode.Error);
                            throw AiErrorConverter.Convert(
                                500,
                                DeserializeError(item.Data),
                                null);
                        default:
                            onProgress(item);
                            break;
                    }
                }

                activity?.SetStatus(ActivityStatusCode.Error);
                throw new AiRequestInterruptedException();
            },
            token);

        if (notifyJobsChanged)
        {
            jobChangeNotifier.Notify();
        }

        return response.Value;
    }

    private static async Task<AiException> ToFailureAsync(
        HttpResponseMessage message,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        string body = string.Empty;
        try
        {
            body = await ReadResponseBodyAsync(message.Content, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A body that cannot be read says nothing more than the status did.
        }

        return AiErrorConverter.Convert(
            (int)message.StatusCode,
            DeserializeError(body),
            null,
            $"The AI request failed ({(int)message.StatusCode}).");
    }

    private static async Task<string> ReadResponseBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBodyBytes)
            throw new AiException("The AI response body exceeded the supported size.");

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        int initialCapacity = content.Headers.ContentLength is > 0 and <= MaximumResponseBodyBytes
            ? (int)content.Headers.ContentLength.Value
            : 0;
        using var body = new MemoryStream(initialCapacity);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int totalBytes = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                if (totalBytes > MaximumResponseBodyBytes - read)
                    throw new AiException("The AI response body exceeded the supported size.");

                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        body.Position = 0;
        Encoding encoding = Encoding.UTF8;
        if (content.Headers.ContentType?.CharSet is { Length: > 0 } charset)
        {
            encoding = Encoding.GetEncoding(charset.Trim().Trim('"'));
        }

        using var reader = new StreamReader(
            body,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>The same JSON body Refit would have sent, as a raw request.</summary>
    protected HttpRequestMessage JsonRequest<TBody>(
        string path,
        string idempotencyKey,
        TBody body)
    {
        HttpRequestMessage request = CreateIdempotentPost(path, idempotencyKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, AiStreamJson.Options),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    protected HttpRequestMessage JsonRequestBytes(
        string path,
        string idempotencyKey,
        ReadOnlyMemory<byte> body)
    {
        HttpRequestMessage request = CreateIdempotentPost(path, idempotencyKey);
        request.Content = new ByteArrayContent(body.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return request;
    }

    private HttpRequestMessage CreateIdempotentPost(string path, string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(Application.HttpClient.BaseAddress!, path));
        request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
        return request;
    }

    // Read leniently: what matters is the code and the message, and a body that
    // carries them but not every field the client's own record insists on is
    // still the failure it says it is.
    private sealed record ErrorBody
    {
        [System.Text.Json.Serialization.JsonPropertyName("error_code")]
        public ApiErrorCode? ErrorCode { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; init; }
    }

    private static ApiErrorResponse? DeserializeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            ErrorBody? parsed = JsonSerializer.Deserialize<ErrorBody>(
                body,
                AiStreamJson.Options);
            if (parsed is null)
                return null;
            return new ApiErrorResponse
            {
                ErrorCode = parsed.ErrorCode ?? ApiErrorCode.Unknown,
                Message = parsed.Message,
                DocumentationUrl = null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected async Task<TResult> ExecuteAsync<TResponse, TResult>(
        string activityName,
        Func<string, CancellationToken, Task<TResponse>> send,
        Func<TResponse, TResult> map,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        bool notifyJobsChanged = true)
    {
        using CancellationTokenSource operationCts =
            Application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = Application.ActivitySource.StartActivity(
            activityName,
            ActivityKind.Client);
        configureActivity?.Invoke(activity);

        try
        {
            AuthenticatedApiResult<TResponse> response =
                await Application.SendAuthenticatedAsync(send, token);
            TResult result = map(response.Value);
            if (notifyJobsChanged)
            {
                jobChangeNotifier.Notify();
            }
            return result;
        }
        catch (ApiException ex)
        {
            throw await AiErrorConverter.ConvertAsync(ex, activity);
        }
    }
}
