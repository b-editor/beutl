using Beutl.Api.Services;
using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

internal static class AiRetryTestContext
{
    public static AiRetryAttemptContext Create()
        => new(
            new FileAiRetryKeyStore(Path.Combine(
                Path.GetTempPath(),
                "Beutl.HeadlessUITests",
                "retry-keys",
                Guid.NewGuid().ToString("N"))),
            () => new AiAuthenticatedRequestIdentity("headless-account", User: null),
            allowSyntheticIdentity: true);

    public static AiRequestRecoveryContext CreateForm()
        => new(
            new FileAiRequestRecoveryStore(Path.Combine(
                Path.GetTempPath(),
                "Beutl.HeadlessUITests",
                "ai-request-recovery",
                Guid.NewGuid().ToString("N"))),
            () => new AiAuthenticatedRequestIdentity("test-user", User: null));

}
