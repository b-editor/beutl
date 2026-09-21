using System.Net;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    [TestCase("image", "png")]
    [TestCase("video", "mp4")]
    [TestCase("audio", "wav")]
    [TestCase("source", "mp4")]
    [TestCase("character", "png")]
    public async Task VideoInputRename_ReusesThePendingKeyForIdenticalContent(string role, string extension)
    {
        await TestReset.ResetShellAsync();
        var sent = new List<(string Key, string Body)>();
        using var handler = new StubHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith("/api/v3/ai/videos", StringComparison.Ordinal))
                sent.Add((request.Headers.GetValues("Idempotency-Key").Single(), await request.Content!.ReadAsStringAsync(token)));
            return ProviderVideoResponse(request);
        });
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        AiSourceVideoMode? mode = role switch
        {
            "source" => AiSourceVideoMode.Edit,
            "character" => AiSourceVideoMode.Motion,
            _ => null,
        };
        await using var vm = CreateVideoGenerationDialog(clients, sourceMode: mode);
        await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
        string original = Path.Combine(BeutlHomeIsolation.CurrentHome!, "original-input." + extension);
        string renamed = Path.Combine(BeutlHomeIsolation.CurrentHome!, "renamed-input." + extension);
        string motionSource = Path.Combine(BeutlHomeIsolation.CurrentHome!, "motion-source.mp4");
        foreach (string file in new[] { original, renamed, motionSource }) await File.WriteAllBytesAsync(file, [1, 2, 3]);
        string selected = original;
        vm.VideoDurationReader = _ => TimeSpan.FromSeconds(7);
        vm.InputPicker = (kind, _) => Task.FromResult<IReadOnlyList<string>>([role == "character" && kind == "source" ? motionSource : selected]);
        if (mode is not null) await vm.SelectSourceVideo.ExecuteAsync();
        if (role == "character") await vm.SelectCharacterImage.ExecuteAsync();
        if (mode is null) await vm.ReferenceGroups.Single(group => group.Kind == role).Pick.ExecuteAsync();
        vm.Prompt.Value = "keep the scene";
        await WaitUntilAsync(() => vm.CanGenerate.Value);
        await vm.Generate.ExecuteAsync();
        Assert.That(sent, Has.Count.EqualTo(1), vm.Error.Value);
        selected = renamed;
        if (role == "source") await vm.SelectSourceVideo.ExecuteAsync();
        else if (role == "character") await vm.SelectCharacterImage.ExecuteAsync();
        else
        {
            var group = vm.ReferenceGroups.Single(group => group.Kind == role);
            group.Files.Single().Remove.Execute();
            await group.Pick.ExecuteAsync();
        }
        await vm.Generate.ExecuteAsync();
        Assert.That(sent, Has.Count.EqualTo(2), vm.Error.Value);
        Assert.That(sent[0].Body, Does.Contain(Path.GetFileName(original)));
        Assert.That(sent[1].Body, Does.Contain(Path.GetFileName(renamed)));
        Assert.That(sent[1].Key, Is.EqualTo(sent[0].Key));
    }
}
