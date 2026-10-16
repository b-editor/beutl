using System.Net;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    // A clip's transcription sends the clip's span of its file, from its offset and at its speed, in parts of
    // at most ten minutes, so the estimate prices that part of the file rather than the whole file. The cases: a
    // 30-second clip of an hour-long file, a clip that starts 40 seconds into a minute-long file, a 30-second clip at
    // double speed, and a 30-minute clip, priced one 10-minute part at a time.
    [AvaloniaTest]
    [TestCase(3600, 30, 0, 100, 30)]
    [TestCase(60, 50, 40, 100, 20)]
    [TestCase(3600, 30, 0, 200, 60)]
    [TestCase(3600, 1800, 0, 100, 600)]
    public async Task Transcription_estimate_for_a_file_prices_the_clip(
        double fileSeconds, double clipSeconds, double offsetSeconds, float speed, double expectedSeconds)
    {
        await TestReset.ResetShellAsync();
        var availability = new RecordingAvailabilityService();
        await using var api = new ApiScope(new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        }));
        BeutlApiApplication clients = api.Clients;
        await using var viewModel = CreateSubtitleDialog(clients, availabilityService: availability);

        viewModel.SelectedAudioSource.Value = new AudioSourceItem(
            "clip",
            Path.Combine(BeutlHomeIsolation.CurrentHome!, "recording.wav"),
            TimeSpan.FromSeconds(fileSeconds),
            elementLength: TimeSpan.FromSeconds(clipSeconds),
            sourceOffset: TimeSpan.FromSeconds(offsetSeconds),
            speed: speed);
        AiOperationAvailabilityRequest.Transcription[] requests = [];
        await WaitUntilAsync(() => (requests = availability.GetRequestsSnapshot()
            .OfType<AiOperationAvailabilityRequest.Transcription>().ToArray()).Length > 0);

        Assert.That(requests.Last().DurationSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }
}
