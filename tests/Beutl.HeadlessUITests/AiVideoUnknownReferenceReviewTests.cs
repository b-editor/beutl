using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    [AvaloniaTest]
    public async Task Review_UnknownReferenceRoleDoesNotThrowOrMutateCurrentInputs()
    {
        await TestReset.ResetShellAsync();
        using var handler = new StubHandler(ProviderVideoResponse);
        using var http = new HttpClient(handler);
        await using var clients = new BeutlApiApplication(http, new ExtensionProvider());
        SetAuthenticatedUser(clients, http);
        using var context = CreateIdentityContext(() => "test-user");
        await using var vm = CreateVideoGenerationDialog(clients, context: context);
        await WaitUntilAsync(() => vm.ModelPicker.IsLoaded.Value);
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "existing.png");
        byte[] bytes = [1, 2, 3];
        await File.WriteAllBytesAsync(path, bytes);
        vm.Prompt.Value = "keep this prompt";
        var group = vm.ReferenceGroups.Single(item => item.Kind == "image");
        group.Add(path);
        var source = FileAiRequestRecoveryStore.CreateExternalSource("reference-depth-0", path, "existing.png", bytes);
        var attempt = new AiPendingAttempt("test-user", "video.generate", "unknown-role", "unknown-key",
            Form: new AiRequestFormSnapshot(Prompt: "different prompt"), Sources: [source]);
        context.Store.WriteOrGet(attempt);
        bool restored = true;
        Assert.DoesNotThrow(() => restored = vm.TryRecoverPendingAttempt(attempt));
        Assert.That(restored, Is.False);
        Assert.That(vm.Prompt.Value, Is.EqualTo("keep this prompt"));
        Assert.That(group.Files.Single().Path, Is.EqualTo(path));
        Assert.That(context.Store.Find("test-user", "video.generate", "unknown-role"), Is.Not.Null);
    }
}
