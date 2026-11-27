using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.AgentToolkit.Live;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class BeutlAgentWorkspaceHostTests
{
    [AvaloniaTest]
    public async Task OpenTabsRefreshProvidersAndApplyDefaultsToNewThreads()
    {
        var (project, _, editor) = await TestShell.OpenNewSceneAsync("Agent Settings", 720, 480, 24,
            duration: MediaTime.FromSeconds(6));
        string home = BeutlHomeIsolation.CurrentHome!;
        string missingCommand = Path.Combine(home, "missing-agent-executable");
        var config = new AiAgentConfig
        {
            ClaudeCodeExecutablePath = { Value = missingCommand },
            CodexExecutablePath = { Value = missingCommand },
            DefaultRuntimeProviderId = { Value = "codex" },
            StdioCommand = { Value = "test-mcp" }
        };
        await using var endpoint = new AgentHostEndpoint(TestShell.Project, TestShell.Editor,
            UnavailableAgentAiBackend.Instance, AgentHostTestHelpers.AvailablePort(), "stage3-test-token",
            registryDirectory: Path.Combine(home, "stage3-endpoint"));
        var bridge = new AgentTabTestBridge();
        await using var runtime = new AgentRuntimeManager(bridge, new AgentThreadStore(home));
        var workspace = new BeutlAgentWorkspaceHost(runtime, endpoint, config, home);
        using var settings = new AiAgentSettingsPageViewModel(config);
        using var left = workspace.CreateWorkspace(editor)!;
        using var right = workspace.CreateWorkspace(editor)!;
        await Task.WhenAll(left.InitializeAsync(), right.InitializeAsync());
        Assert.That(left.ProviderIsAvailable, Is.False);

        settings.RuntimeSettings.CodexExecutablePath.Value = Environment.ProcessPath!;
        await HeadlessTestHelpers.WaitUntilAsync(() => left.ProviderIsAvailable && right.ProviderIsAvailable);

        var lockedThread = await runtime.CreateThreadAsync(project.Uri!.LocalPath, workspace.GetProviders()[1],
            "Existing conversation", AgentPermissionMode.Supervised, "previous-model");
        await left.SelectThreadAsync(lockedThread);
        settings.RuntimeSettings.NewProviderId.Value = "local-acp";
        settings.RuntimeSettings.NewProviderName.Value = "Local agent";
        settings.RuntimeSettings.NewProviderCommand.Value = Environment.ProcessPath!;
        settings.RuntimeSettings.AddProvider.Execute();
        settings.RuntimeSettings.DefaultProvider.Value = settings.RuntimeSettings.ProviderChoices.Value.Single(p => p.Id == "local-acp");
        settings.RuntimeSettings.DefaultPermission.Value = settings.RuntimeSettings.PermissionChoices.Single(p => p.Mode == AgentPermissionMode.AutoAcceptEdits);
        settings.RuntimeSettings.DefaultModel.Value = "new-model";
        await HeadlessTestHelpers.WaitUntilAsync(() => left.Providers.Any(p => p.Descriptor.Id == "local-acp" && p.IsAvailable)
            && right.SelectedProvider?.Descriptor.Id == "local-acp" && right.SelectedPermissionMode.Mode == AgentPermissionMode.AutoAcceptEdits);

        Assert.Multiple(() =>
        {
            Assert.That(left.SelectedProvider!.Descriptor.Id, Is.EqualTo("codex"));
            Assert.That(left.SelectedPermissionMode.Mode, Is.EqualTo(AgentPermissionMode.Supervised));
            Assert.That(left.SelectedThread!.Model, Is.EqualTo("previous-model"));
            Assert.That(right.SelectedThread, Is.Null);
        });
        const string prompt = "$beutl-agent-look-effect-chain Check the current scene";
        AgentTurnRequest? sent = null;
        bridge.Script = (request, _) =>
        {
            sent = request;
            return Task.CompletedTask;
        };
        right.Prompt = prompt;
        await right.SendAsync();
        Assert.That(bridge.OpenedSession!.Model, Is.EqualTo("new-model"));
        Assert.That(workspace.BundledSkills.Any(skill => skill.Name == "beutl-agent-look-effect-chain"
            && skill.Kind == AgentComposerSuggestionKind.Skill && !string.IsNullOrWhiteSpace(skill.Description)), Is.True);
        Assert.That(sent!.Text, Is.EqualTo(prompt));
        Assert.That(sent.BundledSkills!.Single().Name, Is.EqualTo("beutl-agent-look-effect-chain"));
        Assert.That(sent.BundledSkills!.Single().Instructions, Does.Contain("# Beutl"));

        settings.RuntimeSettings.RemoveProvider("local-acp");
        await HeadlessTestHelpers.WaitUntilAsync(() => left.Providers.All(p => p.Descriptor.Id != "local-acp")
            && right.SelectedProvider is { IsAvailable: false, Descriptor.Id: "local-acp" });
        Assert.That(right.SelectedThread!.ProviderId, Is.EqualTo("local-acp"));
    }

    [AvaloniaTest]
    public async Task InjectsEditorContextAndRecordsOnlyTheAttributedLiveMcpEdit()
    {
        var (project, scene, editor) = await TestShell.OpenNewSceneAsync("Agent Context", 720, 480, 24,
            duration: MediaTime.FromSeconds(6));
        string originalSceneName = scene.Name.Value;
        var element = new Element { Name = { Value = "Selected title" }, Length = { Value = MediaTime.FromSeconds(2) } };
        scene.Children.Add(element);
        editor.HistoryManager.Commit("Set up the scene");
        editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(1.5));
        editor.GetRequiredService<IEditorSelection>().SelectedElements.Add(element);

        string home = BeutlHomeIsolation.CurrentHome!;
        var config = new AiAgentConfig
        {
            StdioCommand = { Value = "custom-mcp" },
            StdioArguments = { Value = "--option\r\nvalue with spaces" },
            CodexExecutablePath = { Value = Environment.ProcessPath! },
            CustomRuntimeProvidersJson = { Value = """[{"id":"custom","displayName":"Custom","command":"acp","arguments":["serve"]}]""" }
        };
        await using var endpoint = new AgentHostEndpoint(TestShell.Project, TestShell.Editor,
            UnavailableAgentAiBackend.Instance, AgentHostTestHelpers.AvailablePort(), "stage2-test-token",
            registryDirectory: Path.Combine(home, "stage2-endpoint"));
        var bridge = new AgentTabTestBridge();
        await using var runtime = new AgentRuntimeManager(bridge, new AgentThreadStore(home));
        var workspace = new BeutlAgentWorkspaceHost(runtime, endpoint, config, home);
        var providers = workspace.GetProviders();
        Assert.That(providers.Select(provider => provider.Id), Is.EqualTo(new[] { "claude", "codex", "custom" }));
        var provider = providers[1];
        string projectPath = project.Uri!.LocalPath;
        var thread = await runtime.CreateThreadAsync(projectPath, provider, "Live test", workspace.DefaultPermissionMode);
        var attachments = new[] { new AgentAttachedFile(Path.Combine(home, "frame.png"), "image/png") };
        var callbacks = new AgentTurnCallbacks(_ => { }, (_, _) => Task.FromResult(AgentPermissionDecision.RejectOnce),
            (_, _) => Task.FromResult<IReadOnlyDictionary<string, string[]>>(new Dictionary<string, string[]>()));
        AgentContextSnapshot? observedContext = null;

        bridge.Script = async (request, ct) =>
        {
            observedContext = request.Context;
            var mcp = (StdioMcpServerSpec)bridge.OpenedSession!.McpServers.Single();
            await endpoint.StartAsync(ct);
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.EndpointUri!,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer " + endpoint.Token,
                    [AgentHostPeerClient.AgentThreadHeader] = mcp.Env![AgentHostPeerClient.AgentThreadEnvironmentVariable]
                }
            }), cancellationToken: ct);
            var result = await client.CallToolAsync("apply_edit", new Dictionary<string, object?>
            {
                ["sceneId"] = scene.Id.Value.ToString("D"),
                ["schemaVersion"] = "1",
                ["patch"] = new JsonObject { ["Name"] = "Edited live" }
            }, cancellationToken: ct);
            AgentHostTestHelpers.Success(result);
        };

        var turn = new AgentTurnRequest(thread.Id, Guid.NewGuid(), "Rename the scene");
        await workspace.RunTurnAsync(editor, projectPath, provider, turn, true, attachments, null, callbacks, CancellationToken.None);
        var server = (StdioMcpServerSpec)bridge.OpenedSession!.McpServers.Single();
        Assert.Multiple(() =>
        {
            Assert.That(endpoint.IsRunning, Is.True);
            Assert.That(server.Name, Is.EqualTo("beutl"));
            Assert.That(server.Command, Is.EqualTo("custom-mcp"));
            Assert.That(server.Args, Is.EqualTo(new[] { "--option", "value with spaces" }));
            Assert.That(server.Env!["BEUTL_HOME"], Is.EqualTo(home));
            Assert.That(server.Env.Values, Does.Not.Contain(endpoint.Token));
            Assert.That(observedContext!.InstanceId, Is.EqualTo(endpoint.InstanceId));
            Assert.That(observedContext.ProjectPath, Is.EqualTo(projectPath));
            Assert.That(observedContext.ProjectName, Is.EqualTo(Path.GetFileNameWithoutExtension(projectPath)));
            Assert.That(observedContext.SceneId, Is.EqualTo(scene.Id.Value.ToString("D")));
            Assert.That(observedContext.SceneName, Is.EqualTo(originalSceneName));
            Assert.That(observedContext.FrameWidth, Is.EqualTo(720));
            Assert.That(observedContext.FrameHeight, Is.EqualTo(480));
            Assert.That(observedContext.FrameRate, Is.EqualTo(24));
            Assert.That(observedContext.DurationSeconds, Is.EqualTo(6));
            Assert.That(observedContext.PlayheadSeconds, Is.EqualTo(1.5));
            Assert.That(observedContext.SelectedElementIds, Is.EqualTo(new[] { element.Id.Value.ToString("D") }));
            Assert.That(observedContext.AttachedFiles, Is.EqualTo(attachments));
            Assert.That(scene.Name.Value, Is.EqualTo("Edited live"));
        });

        var entries = await runtime.LoadTimelineAsync(projectPath, thread.Id);
        Assert.That(entries.OfType<AgentTurnHistoryRecorded>().Single().TransactionCount, Is.EqualTo(1));
        Assert.That(workspace.CanRevertTurn(editor, turn.TurnId), Is.True);
        Assert.That(await workspace.RevertTurnAsync(editor, turn.TurnId, CancellationToken.None), Is.EqualTo(AgentTurnRevertResult.Reverted));
        Assert.That(scene.Name.Value, Is.EqualTo(originalSceneName));

        bridge.Script = (request, _) =>
        {
            observedContext = request.Context;
            return Task.CompletedTask;
        };
        await workspace.RunTurnAsync(editor, projectPath, provider, turn with { TurnId = Guid.NewGuid() },
            false, attachments, null, callbacks, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(observedContext!.SceneId, Is.Null);
            Assert.That(observedContext.PlayheadSeconds, Is.Null);
            Assert.That(observedContext.SelectedElementIds, Is.Empty);
            Assert.That(observedContext.AttachedFiles, Is.EqualTo(attachments));
        });
    }
}
