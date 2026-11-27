using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using static Beutl.HeadlessUITests.AgentHostTestHelpers;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentHostProjectWorkflowTests
{
    [AvaloniaTest]
    public async Task Template_save_uses_the_live_selection_and_insert_has_one_undo_step()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("AgentTemplate");
        var element = new Element
        {
            Name = { Value = "Selected source" },
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "source.belm"))
        };
        editor.HistoryManager.ExecuteInTransaction(() => scene.Children.Add(element), "Add source");
        editor.GetRequiredService<IEditorSelection>().Select(element);
        int historyBefore = editor.HistoryManager.UndoCount;

        await using AgentHostEndpoint host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        var save = Target(scene);
        save["name"] = "Selected template";
        JsonObject saved = Success(await client.CallToolAsync("save_element_template", save));
        string templateId = saved["templateId"]!.GetValue<string>();
        Assert.That(Success(await client.CallToolAsync("list_element_templates"))["templates"]!.AsArray()
            .Any(item => item!["templateId"]!.GetValue<string>() == templateId), Is.True);
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(historyBefore));

        var add = Target(scene);
        add["templateId"] = templateId;
        add["timeSeconds"] = 2;
        add["layer"] = 1;
        JsonObject added = Success(await client.CallToolAsync("add_element_from_template", add));
        Element copy = scene.Children.Single(item => item != element);
        Assert.Multiple(() =>
        {
            Assert.That(added["createdElementIds"]!.AsArray().Single()!.GetValue<string>(), Is.EqualTo(copy.Id.Value.ToString()));
            Assert.That(copy.Name.Value, Is.EqualTo(element.Name.Value));
            Assert.That(copy.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(copy.ZIndex.Value, Is.EqualTo(1));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(historyBefore + 1));
        });
        Success(await client.CallToolAsync("undo", Target(scene)));
        Assert.That(scene.Children.Single(), Is.SameAs(element));
    }

    [AvaloniaTest]
    public async Task Scene_removal_rejects_the_last_and_dirty_scene_and_persists_only_membership()
    {
        (Project project, Scene first, EditViewModel editor) = await TestShell.OpenNewSceneAsync("AgentRemoveScene");
        await using AgentHostEndpoint host = CreateHost();
        await host.StartAsync();
        await using McpClient client = await ConnectAsync(host);
        AssertError(await client.CallToolAsync("remove_scene", Target(first)), ErrorCode.ValidationRejected);

        Scene second = ProjectOperations.AddScene(project,
            new SceneCreateOptions(640, 480, TimeSpan.Zero, TimeSpan.FromSeconds(5), "Second"));
        ProjectOperations.Save(project);
        bool autoSaveSuppressed = EditViewModel.IsAutoSaveSuppressedForTesting;
        try
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            editor.HistoryManager.ExecuteInTransaction(() => first.Name.Value = "Unsaved", "Rename");
            var refused = await client.CallToolAsync("remove_scene", Target(first));
            AssertError(refused, ErrorCode.ValidationRejected);
            Assert.That(Payload(refused)["error"]!["message"]!.GetValue<string>(), Does.Contain("Save"));
            Assert.That(project.Items, Has.Count.EqualTo(2));

            Assert.That(await editor.SaveAsync(), Is.True);
            byte[] removedScene = File.ReadAllBytes(first.Uri!.LocalPath);
            byte[] remainingScene = File.ReadAllBytes(second.Uri!.LocalPath);
            Success(await client.CallToolAsync("remove_scene", Target(first)));
            Project reloaded = CoreSerializer.RestoreFromUri<Project>(project.Uri!);
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Items.OfType<Scene>().Single().Id.Value, Is.EqualTo(second.Id.Value));
                Assert.That(File.ReadAllBytes(first.Uri.LocalPath), Is.EqualTo(removedScene));
                Assert.That(File.ReadAllBytes(second.Uri.LocalPath), Is.EqualTo(remainingScene));
            });
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = autoSaveSuppressed;
        }
    }

    [AvaloniaTest]
    public async Task Live_commit_saves_the_editor_and_returns_the_snapshot_from_the_version_control_service()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false, autoCommitOnClose: false, useLfsWhenAvailable: false);
        Project project = await git.CreateProjectAsync("agent-live-vcs", "Workflow");
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        bool autoSaveSuppressed = EditViewModel.IsAutoSaveSuppressedForTesting;
        try
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            editor.HistoryManager.ExecuteInTransaction(() => scene.Name.Value = "Saved by MCP", "Rename");
            await using AgentHostEndpoint host = CreateHost();
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            JsonObject status = Success(await client.CallToolAsync("read_version_control_status", Target(scene)));
            Assert.That(status["hasRepository"]!.GetValue<bool>(), Is.True);

            var commit = Target(scene);
            commit["message"] = "Manual agent snapshot";
            JsonObject created = Success(await client.CallToolAsync("commit_project", commit));
            var history = Target(scene);
            history["take"] = 1;
            JsonNode newest = Success(await client.CallToolAsync("read_version_control_history", history))["commits"]!
                .AsArray().Single()!;
            Assert.Multiple(() =>
            {
                Assert.That(created["committed"]!.GetValue<bool>(), Is.True);
                Assert.That(newest["sha"]!.GetValue<string>(), Is.EqualTo(created["sha"]!.GetValue<string>()));
                Assert.That(newest["subject"]!.GetValue<string>(), Is.EqualTo("Manual agent snapshot"));
                Assert.That(CoreSerializer.RestoreFromUri<Scene>(scene.Uri!).Name.Value, Is.EqualTo("Saved by MCP"));
                Assert.That(editor.IsEnabled.Value, Is.True);
            });
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = autoSaveSuppressed;
        }
    }

    [AvaloniaTest]
    public async Task Live_commit_refuses_an_external_merge_before_saving_the_editor()
    {
        await using RealGitScope git = await RealGitScope.StartAsync(
            autoCommitOnSave: false, autoCommitOnClose: false, useLfsWhenAvailable: false);
        Project project = await git.CreateProjectAsync("agent-live-vcs-merge", "Workflow");
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        string root = Path.GetDirectoryName(project.Uri!.LocalPath)!;
        string scenePath = scene.Uri!.LocalPath;
        string sceneEntry = Path.GetRelativePath(root, scenePath).Replace('\\', '/');
        string branch = (await GitTestSupport.RunGitAsync(git.GitPath, root, "branch", "--show-current")).Trim();
        await GitTestSupport.RunGitAsync(git.GitPath, root, "switch", "-c", "incoming");
        File.WriteAllText(scenePath, "Incoming scene changes\n");
        await GitTestSupport.RunGitAsync(git.GitPath, root, "add", "--", sceneEntry);
        await GitTestSupport.RunGitAsync(git.GitPath, root, "commit", "-m", "Incoming changes");
        await GitTestSupport.RunGitAsync(git.GitPath, root, "switch", branch);
        await GitTestSupport.RunGitAsync(git.GitPath, root, "merge", "--no-ff", "--no-commit", "incoming");

        byte[] projectBefore = File.ReadAllBytes(project.Uri.LocalPath);
        byte[] sceneBefore = File.ReadAllBytes(scenePath);
        bool autoSaveSuppressed = EditViewModel.IsAutoSaveSuppressedForTesting;
        try
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = true;
            editor.HistoryManager.ExecuteInTransaction(() => scene.Name.Value = "Pending editor change", "Rename");
            await using AgentHostEndpoint host = CreateHost();
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);
            var arguments = Target(scene);
            arguments["message"] = "Preserve the merge";

            var refused = await client.CallToolAsync("commit_project", arguments);
            AssertError(refused, ErrorCode.ValidationRejected);
            Assert.That(Payload(refused)["error"]!["message"]!.GetValue<string>(), Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            await Assert.ThatAsync(() => TestShell.VersionControl.CommitManualAsync("Manual commit"),
                Throws.TypeOf<VersionControlConflictedException>());
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllBytes(project.Uri.LocalPath), Is.EqualTo(projectBefore));
                Assert.That(File.ReadAllBytes(scenePath), Is.EqualTo(sceneBefore));
                Assert.That(editor.HasUnsavedSceneChanges, Is.True);
                Assert.That(editor.IsEnabled.Value, Is.True);
            });
        }
        finally
        {
            EditViewModel.IsAutoSaveSuppressedForTesting = autoSaveSuppressed;
        }
    }

    private static AgentHostEndpoint CreateHost()
        => new(TestShell.Project, TestShell.Editor, UnavailableAgentAiBackend.Instance, new AiAgentConfig());

    private static Dictionary<string, object?> Target(Scene scene)
        => new() { ["sceneId"] = scene.Id.Value.ToString() };
}
