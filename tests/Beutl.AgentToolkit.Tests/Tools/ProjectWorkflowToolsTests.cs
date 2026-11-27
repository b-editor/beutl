using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed partial class ProjectWorkflowToolsTests
{
    [Test]
    public async Task Stdio_advertises_project_workflow_inputs_and_routes()
    {
        var schemas = new (string Name, string[] Required, string[] Optional, bool ReadOnly)[]
        {
            ("list_element_templates", [], ["instanceId"], true),
            ("save_element_template", ["name"], ["elementIds", "instanceId", "sceneId"], false),
            ("add_element_from_template", ["templateId", "timeSeconds", "layer"], ["instanceId", "sceneId"], false),
            ("list_templates", [], ["baseType", "instanceId"], true),
            ("save_object_template", ["objectId", "name"], ["instanceId", "sceneId"], false),
            ("apply_object_template", ["templateId", "objectId", "propertyName"], ["index", "instanceId", "sceneId"], false),
            ("remove_scene", ["sceneId"], ["instanceId"], false),
            ("read_version_control_status", [], ["instanceId", "sceneId"], true),
            ("read_version_control_history", [], ["skip", "take", "instanceId", "sceneId"], true),
            ("commit_project", ["message"], ["instanceId", "sceneId"], false),
            ("inspect_project_assets", [], ["instanceId", "sceneId"], true),
            ("organize_project_assets", ["action", "paths"], ["confirmDelete", "instanceId", "sceneId"], false)
        };
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        IList<McpClientTool> tools = await server.Client.ListToolsAsync();
        foreach (var expected in schemas)
        {
            McpClientTool tool = tools.Single(tool => tool.Name == expected.Name);
            JsonElement schema = tool.JsonSchema;
            IEnumerable<string?> required = schema.TryGetProperty("required", out JsonElement requiredProperty)
                ? requiredProperty.EnumerateArray().Select(item => item.GetString())
                : [];
            Assert.Multiple(() =>
            {
                Assert.That(tool.Description, Is.Not.Empty, expected.Name);
                Assert.That(required, Is.EquivalentTo(expected.Required), expected.Name);
                Assert.That(schema.GetProperty("properties").EnumerateObject().Select(item => item.Name),
                    Is.EquivalentTo(expected.Required.Concat(expected.Optional)), expected.Name);
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint == true, Is.EqualTo(expected.ReadOnly), expected.Name);
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Template_save_list_and_insert_round_trip_as_one_undo_step(bool multiElement)
    {
        string profile = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(profile);
        FileEditingSession session = await CreateProjectAsync(server);
        Scene scene = session.Scene;
        Element first = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), "first.belm");
        first.Name.Value = "First";
        Element[] originals = [first];
        if (multiElement)
        {
            Element second = TestWorkspace.AddElement(scene, MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), "second.belm");
            second.Name.Value = "Second";
            second.ZIndex.Value = 2;
            originals = [first, second];
            scene.Groups.Add(ImmutableHashSet.Create(first.Id.Value, second.Id.Value));
        }
        session.History.Commit("Seed test elements");
        session.History.Clear();

        ElementTemplateSummary saved = Success(await Call<ElementTemplateSummary>(server.Client, "save_element_template",
            ("name", "Reusable"), ("elementIds", originals.Select(element => element.Id.Value.ToString()).ToArray())));
        ListElementTemplatesResponse listed = Success(await Call<ListElementTemplatesResponse>(server.Client, "list_element_templates"));
        Assert.Multiple(() =>
        {
            Assert.That(saved.IsElementSet, Is.EqualTo(multiElement));
            Assert.That(listed.Templates, Is.EqualTo(new[] { saved }));
            Assert.That(File.Exists(Path.Combine(profile, "templates", "Reusable.json")), Is.True);
            Assert.That(session.History.UndoCount, Is.Zero);
        });

        AddElementFromTemplateResponse added = Success(await Call<AddElementFromTemplateResponse>(
            server.Client, "add_element_from_template", ("templateId", saved.TemplateId), ("timeSeconds", 4.01), ("layer", 3)));
        Element[] copies = scene.Children.Where(element => !originals.Contains(element)).OrderBy(element => element.Start.Value).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(added.CreatedElementIds, Is.EquivalentTo(copies.Select(element => element.Id.Value.ToString())));
            Assert.That(copies.Select(element => element.Id.Value), Has.None.Matches<Guid>(id => originals.Any(element => element.Id.Value == id)));
            Assert.That(copies.Select(element => element.Name.Value), Is.EqualTo(originals.Select(element => element.Name.Value)));
            Assert.That(copies.Select(element => element.Start.Value),
                Is.EqualTo(originals.Select(element => element.Start.Value + MediaTime.FromSeconds(3))));
            Assert.That(copies.Select(element => element.ZIndex.Value), Is.EqualTo(originals.Select(element => element.ZIndex.Value + 3)));
            Assert.That(session.IsDirty, Is.True);
            Assert.That(session.History.UndoCount, Is.EqualTo(1));
        });
        if (multiElement)
            Assert.That(scene.Groups.Any(group => group.SetEquals(copies.Select(element => element.Id.Value))), Is.True);

        await server.Client.CallToolAsync("undo");
        Assert.That(scene.Children, Is.EquivalentTo(originals));
    }

    [Test]
    public async Task Saving_the_same_template_name_through_two_profile_services_keeps_both_files()
    {
        string profile = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(profile);
        FileEditingSession session = await CreateProjectAsync(server);
        Element source = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        string directory = Path.Combine(profile, "templates");
        using var otherTemplates = new ObjectTemplateService(SceneRenderServices.Empty, directory);

        Task<ObjectTemplateItem?> otherSave = otherTemplates.AddFromInstanceAsync(source, "Shared name").AsTask();
        ElementTemplateSummary saved = Success(await Call<ElementTemplateSummary>(server.Client, "save_element_template",
            ("name", "Shared name"), ("elementIds", new[] { source.Id.Value.ToString() })));
        ObjectTemplateItem? other = await otherSave;

        using var reloaded = new ObjectTemplateService(SceneRenderServices.Empty, directory);
        Assert.Multiple(() =>
        {
            Assert.That(other, Is.Not.Null);
            Assert.That(reloaded.FindByBaseType(typeof(Element)).Select(item => item.Id.ToString()),
                Is.EquivalentTo(new[] { saved.TemplateId, other!.Id.ToString() }));
            Assert.That(Directory.GetFiles(directory).Select(Path.GetFileName),
                Is.EquivalentTo(new[] { "Shared name.json", "Shared name (2).json" }));
        });
    }

    [Test]
    public async Task Template_insert_refuses_locked_requested_layer()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        Element element = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        session.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });
        session.History.Commit("Seed");
        session.History.Clear();
        ElementTemplateSummary saved = Success(await Call<ElementTemplateSummary>(server.Client, "save_element_template",
            ("name", "Reusable"), ("elementIds", new[] { element.Id.Value.ToString() })));

        ToolResult<AddElementFromTemplateResponse> result = await Call<AddElementFromTemplateResponse>(
            server.Client, "add_element_from_template", ("templateId", saved.TemplateId), ("timeSeconds", 2), ("layer", 2));
        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Target, Is.EqualTo("layer"));
            Assert.That(session.Scene.Children, Has.Count.EqualTo(1));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    [Test]
    public async Task Removing_a_scene_refuses_the_last_and_unsaved_scenes_then_keeps_the_saved_scene_file()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        Scene first = session.Scene;
        Assert.That((await Call<RemoveSceneResponse>(server.Client, "remove_scene", ("sceneId", first.Id.Value.ToString())))
            .Error?.Message, Does.Contain("last scene"));

        AddSceneResponse second = Success(await Call<AddSceneResponse>(server.Client, "add_scene",
            ("session", session.SessionId), ("width", 640), ("height", 360), ("start", "00:00:00"), ("duration", "00:00:05")));
        Scene active = session.Scene;
        session.EditOnSession(() =>
        {
            active.Name.Value = "Unsaved scene edit";
            session.History.Commit("Rename");
            session.MarkDirty();
            return true;
        });
        Assert.That((await Call<RemoveSceneResponse>(server.Client, "remove_scene", ("sceneId", second.SceneId)))
            .Error?.Message, Does.Contain("Save"));
        Assert.That(session.Project.Items, Has.Count.EqualTo(2));

        Success(await Call<SaveProjectResponse>(server.Client, "save_project"));
        byte[] savedScene = File.ReadAllBytes(active.Uri!.LocalPath);
        Assert.That(Success(await Call<RemoveSceneResponse>(server.Client, "remove_scene",
            ("sceneId", second.SceneId))).SceneId, Is.EqualTo(second.SceneId));
        Assert.That(session.Scene, Is.SameAs(first));
        Success(await Call<SaveProjectResponse>(server.Client, "save_project"));
        Project reloaded = CoreSerializer.RestoreFromUri<Project>(session.Project.Uri!);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Items.OfType<Scene>().Select(scene => scene.Id.Value), Is.EqualTo(new[] { first.Id.Value }));
            Assert.That(File.ReadAllBytes(active.Uri.LocalPath), Is.EqualTo(savedScene));
        });
    }

    [Test]
    public async Task Version_control_reads_status_and_history_and_commits_a_pending_project_edit()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        string root = await InitializeGitAsync(session);
        string initialSha = await RunGitAsync(root, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(root, "note.txt"), "Changed on disk.");
        session.EditOnSession(() =>
        {
            session.Scene.Name.Value = "Saved by commit";
            session.History.Commit("Rename");
            session.MarkDirty();
            return true;
        });

        VersionControlStatusResponse status = Success(await Call<VersionControlStatusResponse>(server.Client, "read_version_control_status"));
        VersionControlHistoryResponse before = Success(await Call<VersionControlHistoryResponse>(server.Client, "read_version_control_history"));
        Assert.Multiple(() =>
        {
            Assert.That(status.HasRepository, Is.True);
            Assert.That(status.HeadCommit, Is.EqualTo(initialSha));
            Assert.That(status.Changes.Select(change => change.Path), Does.Contain("note.txt"));
            Assert.That(before.Commits.Single().Sha, Is.EqualTo(initialSha));
            Assert.That(session.IsDirty, Is.True);
        });

        CommitProjectResponse result = Success(await Call<CommitProjectResponse>(server.Client, "commit_project", ("message", "Local snapshot")));
        VersionControlHistoryResponse newest = Success(await Call<VersionControlHistoryResponse>(server.Client, "read_version_control_history", ("take", 1)));
        VersionControlHistoryResponse older = Success(await Call<VersionControlHistoryResponse>(server.Client, "read_version_control_history", ("skip", 1), ("take", 1)));
        Project reloaded = CoreSerializer.RestoreFromUri<Project>(session.Project.Uri!);
        Assert.Multiple(() =>
        {
            Assert.That(result.Committed, Is.True);
            Assert.That(newest.Commits.Single().Sha, Is.EqualTo(result.Sha));
            Assert.That(newest.Commits.Single().Subject, Is.EqualTo("Local snapshot"));
            Assert.That(older.Commits.Single().Sha, Is.EqualTo(initialSha));
            Assert.That(reloaded.Items.OfType<Scene>().Single().Name.Value, Is.EqualTo("Saved by commit"));
            Assert.That(session.IsDirty, Is.False);
        });
        Assert.That(Success(await Call<CommitProjectResponse>(server.Client, "commit_project", ("message", "No changes"))).Committed, Is.False);
    }

    [Test]
    public async Task Version_control_commit_refuses_signing_and_missing_identity_without_creating_a_commit()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        string root = await InitializeGitAsync(session);
        string initialSha = await RunGitAsync(root, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(root, "note.txt"), "Changed.");
        string scenePath = session.Scene.Uri!.LocalPath;
        byte[] sceneBefore = File.ReadAllBytes(scenePath);
        session.Scene.Name.Value = "Pending scene edit";
        session.MarkDirty();

        await RunGitAsync(root, "config", "commit.gpgSign", "on");
        Assert.That((await Call<CommitProjectResponse>(server.Client, "commit_project", ("message", "Signed")))
            .Error?.Message, Does.Contain("requires Git commit signing"));
        await RunGitAsync(root, "config", "commit.gpgSign", "false");
        await RunGitAsync(root, "config", "user.email", " ");
        Assert.That((await Call<CommitProjectResponse>(server.Client, "commit_project", ("message", "No identity")))
            .Error?.Message, Does.Contain("name and email"));
        Assert.That(await RunGitAsync(root, "rev-parse", "HEAD"), Is.EqualTo(initialSha));
        Assert.That(await RunGitAsync(root, "diff", "--cached", "--name-only"), Is.Empty);
        Assert.That(File.ReadAllBytes(scenePath), Is.EqualTo(sceneBefore));
        Assert.That(session.IsDirty, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Commit_project_preserves_project_files_during_an_external_merge(bool resolveConflicts)
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        Element element = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        session.Save(skipConflictCheck: false);
        string root = await InitializeGitAsync(session);
        string projectPath = session.Project.Uri!.LocalPath;
        string scenePath = session.Scene.Uri!.LocalPath;
        string elementPath = element.Uri!.LocalPath;
        string sceneEntry = Path.GetRelativePath(root, scenePath).Replace('\\', '/');
        string elementEntry = Path.GetRelativePath(root, elementPath).Replace('\\', '/');

        await RunGitAsync(root, "switch", "-c", "incoming");
        File.WriteAllText(scenePath, "Incoming scene change\n");
        File.WriteAllText(elementPath, "Incoming element change\n");
        await RunGitAsync(root, "add", "--", sceneEntry, elementEntry);
        await RunGitAsync(root, "commit", "-m", "Incoming changes");
        await RunGitAsync(root, "switch", "main");
        File.WriteAllText(scenePath, "Local scene change\n");
        File.WriteAllText(elementPath, "Local element change\n");
        await RunGitAsync(root, "add", "--", sceneEntry, elementEntry);
        await RunGitAsync(root, "commit", "-m", "Local changes");
        string previousHead = await RunGitAsync(root, "rev-parse", "HEAD");
        await RunGitAsync(root, 1, "merge", "--no-edit", "incoming");

        if (resolveConflicts)
        {
            File.WriteAllText(scenePath, "Resolved scene\n");
            File.WriteAllText(elementPath, "Resolved element\n");
            await RunGitAsync(root, "add", "--", sceneEntry, elementEntry);
        }
        string unmergedEntries = await RunGitAsync(root, "ls-files", "--unmerged");
        Assert.That(unmergedEntries.Length > 0, Is.EqualTo(!resolveConflicts));
        Assert.That(File.Exists(Path.Combine(root, ".git", "MERGE_HEAD")), Is.True);
        byte[] projectBefore = File.ReadAllBytes(projectPath);
        byte[] sceneBefore = File.ReadAllBytes(scenePath);
        byte[] elementBefore = File.ReadAllBytes(elementPath);
        byte[] indexBefore = File.ReadAllBytes(Path.Combine(root, ".git", "index"));
        session.Scene.Name.Value = "Pending project edit";
        element.Name.Value = "Pending element edit";
        session.MarkDirty();

        ToolResult<CommitProjectResponse> result = await Call<CommitProjectResponse>(
            server.Client, "commit_project", ("message", "Keep merge files"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Is.EqualTo(Strings.VersionControl_ConflictGuidance));
            Assert.That(File.ReadAllBytes(projectPath), Is.EqualTo(projectBefore));
            Assert.That(File.ReadAllBytes(scenePath), Is.EqualTo(sceneBefore));
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(elementBefore));
            Assert.That(File.ReadAllBytes(Path.Combine(root, ".git", "index")), Is.EqualTo(indexBefore));
            Assert.That(session.IsDirty, Is.True);
        });
        Assert.That(await RunGitAsync(root, "rev-parse", "HEAD"), Is.EqualTo(previousHead));
    }

    private static async Task<FileEditingSession> CreateProjectAsync(InProcessAgentToolkitServer server)
    {
        Success(await Call<CreateProjectResponse>(server.Client, "create_project",
            ("path", Path.Combine(TestWorkspace.CreateDirectory(), "Workflow.bep")),
            ("width", 640), ("height", 360), ("frameRate", 30), ("duration", "00:00:10")));
        return (FileEditingSession)server.Services.GetRequiredService<AgentSessionManager>().RequireSession();
    }

    private static async Task<string> InitializeGitAsync(FileEditingSession session)
    {
        string root = Path.GetDirectoryName(session.Project.Uri!.LocalPath)!;
        await RunGitAsync(root, "init", "--initial-branch=main");
        await RunGitAsync(root, "config", "user.name", "Beutl Toolkit Test");
        await RunGitAsync(root, "config", "user.email", "toolkit@example.invalid");
        await RunGitAsync(root, "config", "commit.gpgSign", "false");
        await RunGitAsync(root, "add", ".");
        await RunGitAsync(root, "commit", "-m", "Initial version", "-m", "Beutl-Snapshot: init");
        return root;
    }

    private static Task<string> RunGitAsync(string directory, params string[] arguments)
        => RunGitAsync(directory, 0, arguments);

    private static async Task<string> RunGitAsync(string directory, int expectedExitCode, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process git = Process.Start(start)!;
        Task<string> output = git.StandardOutput.ReadToEndAsync();
        Task<string> errors = git.StandardError.ReadToEndAsync();
        await git.WaitForExitAsync();
        Assert.That(git.ExitCode, Is.EqualTo(expectedExitCode), await errors);
        return (await output).Trim();
    }

    private static async Task<ToolResult<T>> Call<T>(McpClient client, string tool, params (string Name, object? Value)[] args)
        => ToolResultReader.Read<T>(await client.CallToolAsync(tool, args.ToDictionary(arg => arg.Name, arg => arg.Value)));

    private static T Success<T>(ToolResult<T> result)
    {
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }
}
