using System.Diagnostics;
using System.Net;
using System.Text;
using Beutl.Api.Services;
using Refit;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiErrorConverterTests
{
    [TestCase("authenticationIsRequired", typeof(AuthenticationRequiredException))]
    [TestCase("aiPlanRequired", typeof(AiPlanRequiredException))]
    [TestCase("aiUsageLimitExceeded", typeof(AiUsageLimitExceededException))]
    [TestCase("fileIsTooLarge", typeof(AiFileTooLargeException))]
    [TestCase("aiProviderError", typeof(AiProviderErrorException))]
    [TestCase("aiJobIsActive", typeof(AiJobIsActiveException))]
    [TestCase("aiJobLimitReached", typeof(AiJobLimitReachedException))]
    [TestCase("aiRequestInProgress", typeof(AiRequestInProgressException))]
    [TestCase("aiRequestWasDeleted", typeof(AiRequestWasDeletedException))]
    public async Task ConvertAsync_MapsEveryKnownAiError(
        string errorCode,
        Type expectedType)
    {
        ApiException source = await CreateApiException($$"""
            {
              "error_code": "{{errorCode}}",
              "message": "Server message",
              "documentation_url": null
            }
            """);
        using var activity = new Activity("test").Start();

        AiException result = await AiErrorConverter.ConvertAsync(source, activity);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.TypeOf(expectedType));
            Assert.That(result.InnerException, Is.SameAs(source));
            Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Error));
        }
    }

    [TestCase("unknown")]
    [TestCase("doNotHavePermissions")]
    public async Task ConvertAsync_UsesServerMessageForUnmappedCodes(string errorCode)
    {
        ApiException source = await CreateApiException($$"""
            {
              "error_code": "{{errorCode}}",
              "message": "A safe server message",
              "documentation_url": null
            }
            """);

        AiException result = await AiErrorConverter.ConvertAsync(source, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.TypeOf<AiException>());
            Assert.That(result.Message, Is.EqualTo("A safe server message"));
            Assert.That(result.InnerException, Is.SameAs(source));
        }
    }

    [TestCase("{not-json")]
    [TestCase("{\"error_code\":\"futureAiError\",\"message\":\"Future\"}")]
    public async Task ConvertAsync_MalformedOrUnknownPayloadPreservesApiException(
        string content)
    {
        ApiException source = await CreateApiException(content);

        AiException result = await AiErrorConverter.ConvertAsync(source, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.TypeOf<AiException>());
            Assert.That(result.InnerException, Is.SameAs(source));
            Assert.That(result.Data[nameof(Exception)], Is.Null);
            Assert.That(result.Data["parseException"], Is.InstanceOf<Exception>());
        }
    }

    private static async Task<ApiException> CreateApiException(string content)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://beutl.beditor.net/api/v3/ai/images");
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            RequestMessage = request,
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
        return await ApiException.Create(
            request,
            HttpMethod.Post,
            response,
            new RefitSettings());
    }
}
