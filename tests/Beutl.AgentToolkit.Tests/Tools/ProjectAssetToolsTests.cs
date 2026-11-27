using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ProjectAssetToolsTests
{
    [Test]
    public async Task Inspect_reports_asset_categories_and_every_element_using_a_source()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, root) = await CreateProjectAsync(server);
        string external = WriteImage(Path.Combine(TestWorkspace.CreateDirectory(), "external.png"));
        string missing = Path.Combine(Path.GetDirectoryName(external)!, "missing.png");
        string inside = WriteImage(Path.Combine(root, "resources", "used.png"));
        string unused = WriteImage(Path.Combine(root, "resources", "unused.png"));
        string quarantined = WriteImage(Path.Combine(root, EditorConstants.BeutlFolder, "asset-trash",
            session.Project.Id.Value.ToString("N"), Guid.NewGuid().ToString("N"), "quarantined.png"));
        var shared = ImageSource.Open(external);
        Element first = AddImage(session.Scene, "first.belm", shared);
        Element second = AddImage(session.Scene, "second.belm", shared);
        Element local = AddImage(session.Scene, "third.belm", ImageSource.Open(inside));
        Element offline = AddImage(session.Scene, "fourth.belm", ImageSource.Open(missing));
        session.Save(skipConflictCheck: false);

        InspectProjectAssetsResponse result = await InspectAsync(server.Client);
        ProjectAssetEntry outside = result.External.Single(asset => asset.Path == external);
        ProjectAssetEntry missingEntry = result.External.Single(asset => asset.Path == missing);
        ProjectAssetEntry insideEntry = result.ReferencedInsideProject.Single();
        Assert.Multiple(() =>
        {
            Assert.That(outside.Kind, Is.EqualTo("image"));
            Assert.That(outside.SizeBytes, Is.EqualTo(new FileInfo(external).Length));
            Assert.That(outside.IsInsideProject, Is.False);
            Assert.That(outside.ElementIds, Is.EquivalentTo(new[] { first.Id.Value, second.Id.Value }));
            Assert.That(outside.Files, Is.EqualTo(new[] { external }));
            Assert.That(missingEntry.BlockingIssue, Is.EqualTo("Missing"));
            Assert.That(missingEntry.ElementIds, Is.EqualTo(new[] { offline.Id.Value }));
            Assert.That(insideEntry.Path, Is.EqualTo(inside));
            Assert.That(insideEntry.IsInsideProject, Is.True);
            Assert.That(insideEntry.ElementIds, Is.EqualTo(new[] { local.Id.Value }));
            Assert.That(result.Unused.Single().Path, Is.EqualTo(unused));
            Assert.That(result.Unused.Single().ElementIds, Is.Empty);
            Assert.That(result.Quarantined.Single().Path, Is.EqualTo(quarantined));
            Assert.That(result.CleanupIssue, Is.Null);
        });
    }

    [Test]
    public async Task Collect_updates_every_scene_and_saves_without_replacing_an_existing_asset()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, root) = await CreateProjectAsync(server);
        string external = WriteImage(Path.Combine(TestWorkspace.CreateDirectory(), "clip.png"));
        string original = WriteImage(Path.Combine(root, "resources", "clip.png"), SKColors.Green);
        byte[] originalBytes = File.ReadAllBytes(original);
        Scene other = ProjectOperations.AddScene(session.Project,
            new SceneCreateOptions(64, 64, TimeSpan.Zero, TimeSpan.FromSeconds(3), "Other"));
        Element first = AddImage(session.Scene, "first.belm", ImageSource.Open(external));
        Element second = AddImage(other, "second.belm", ImageSource.Open(external));
        session.History.Commit("Add clips");
        session.Save(skipConflictCheck: false);
        int historyCount = session.History.UndoCount;

        OrganizeProjectAssetsResponse result = Success(await OrganizeAsync(server.Client, "collect", [external]));
        string collected = result.Files.Single().Destination!;
        Project reopened = CoreSerializer.RestoreFromUri<Project>(session.Project.Uri!);
        Assert.Multiple(() =>
        {
            Assert.That(result.Files.Single().Path, Is.EqualTo(external));
            Assert.That(collected, Is.EqualTo(Path.Combine(root, "resources", "clip_1.png")));
            Assert.That(File.ReadAllBytes(collected), Is.EqualTo(File.ReadAllBytes(external)));
            Assert.That(File.ReadAllBytes(original), Is.EqualTo(originalBytes));
            Assert.That(SourcePath(first), Is.EqualTo(collected));
            Assert.That(SourcePath(second), Is.EqualTo(collected));
            Assert.That(reopened.Items.OfType<Scene>().SelectMany(scene => scene.Children).Select(SourcePath),
                Is.EqualTo(new[] { collected, collected }));
            Assert.That(session.History.UndoCount, Is.EqualTo(historyCount));
            Assert.That(() => session.Save(skipConflictCheck: false), Throws.Nothing);
        });
    }

    [Test]
    public async Task Failed_collection_discards_all_copies_and_preserves_saved_references()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, root) = await CreateProjectAsync(server);
        string outside = TestWorkspace.CreateDirectory();
        string valid = WriteImage(Path.Combine(outside, "valid.png"));
        string damaged = Path.Combine(outside, "damaged.png");
        File.WriteAllText(damaged, "This is not image data.");
        Element first = AddImage(session.Scene, "first.belm", ImageSource.Open(valid));
        Element second = AddImage(session.Scene, "second.belm", ImageSource.Open(damaged));
        session.Save(skipConflictCheck: false);
        var before = ReadFiles(root);

        ToolResult<OrganizeProjectAssetsResponse> result = await OrganizeAsync(server.Client, "collect", [valid, damaged]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Target, Is.EqualTo(damaged));
            Assert.That(SourcePath(first), Is.EqualTo(valid));
            Assert.That(SourcePath(second), Is.EqualTo(damaged));
            Assert.That(ReadFiles(root), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task Collection_can_be_retried_after_the_document_transaction_rolls_back()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, root) = await CreateProjectAsync(server);
        string path = WriteImage(Path.Combine(TestWorkspace.CreateDirectory(), "clip.png"));
        Element element = AddImage(session.Scene, "clip.belm", ImageSource.Open(path));
        session.Save(skipConflictCheck: false);
        var before = ReadFiles(root);
        var tools = new ProjectAssetTools(server.Services.GetRequiredService<AgentSessionManager>(),
            server.Services.GetRequiredService<IProjectSessionGateway>(), server.Services.GetRequiredService<DecoderRegistry>());
        bool reachedProjectWrite = false;

        // The project write has already staged the relinked sidecars. A source change during
        // this final write fails the commit checks and rolls back all document replacements.
        using (StorageWriteTransaction.InjectFaultsForTesting((step, file) =>
               {
                   if (step != StorageWriteStep.Replace || file != session.Project.Uri!.LocalPath) return;
                   reachedProjectWrite = true;
                   File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(-1));
               }))
        {
            ToolResult<OrganizeProjectAssetsResponse> result = await tools.OrganizeProjectAssets("collect", [path]);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.StaleHandle));
        }
        Assert.Multiple(() =>
        {
            Assert.That(reachedProjectWrite, Is.True);
            Assert.That(ReadFiles(root), Is.EqualTo(before));
            Assert.That(SourcePath(element), Is.EqualTo(path));
        });

        OrganizeProjectAssetsResponse retried = Success(await OrganizeAsync(server.Client, "collect", [path]));
        Assert.That(SourcePath(element), Is.EqualTo(retried.Files.Single().Destination));
    }

    [Test]
    public async Task Quarantine_restore_and_delete_obey_the_editor_eligibility_rules()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, root) = await CreateProjectAsync(server);
        string used = WriteImage(Path.Combine(root, "resources", "used.png"));
        string first = WriteImage(Path.Combine(root, "resources", "nested", "first.png"));
        string second = WriteImage(Path.Combine(root, "resources", "nested", "second.png"));
        Element element = AddImage(session.Scene, "used.belm", ImageSource.Open(used));
        session.Save(skipConflictCheck: false);
        var before = ReadFiles(root);

        Assert.That((await OrganizeAsync(server.Client, "quarantine", [first, used])).Error?.Code,
            Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That(ReadFiles(root), Is.EqualTo(before));
        var moved = Success(await OrganizeAsync(server.Client, "quarantine", [first, second])).Files;
        string firstInTrash = moved.Single(change => change.Path == first).Destination!;
        string secondInTrash = moved.Single(change => change.Path == second).Destination!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(first), Is.False);
            Assert.That(File.Exists(second), Is.False);
            Assert.That(File.ReadAllBytes(firstInTrash), Is.EqualTo(before[first]));
            Assert.That(File.ReadAllBytes(secondInTrash), Is.EqualTo(before[second]));
        });

        WriteImage(second, SKColors.Blue);
        var occupied = ReadFiles(root);
        Assert.That((await OrganizeAsync(server.Client, "restore", [firstInTrash, secondInTrash])).Error?.Code,
            Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That((await OrganizeAsync(server.Client, "delete", [firstInTrash, secondInTrash], confirmDelete: true))
            .Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That((await OrganizeAsync(server.Client, "delete", [secondInTrash])).Error?.Code,
            Is.EqualTo(ErrorCode.DestructiveIntent));
        Assert.That((await OrganizeAsync(server.Client, "delete", [used], confirmDelete: true)).Error?.Code,
            Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That(ReadFiles(root), Is.EqualTo(occupied));

        var restored = Success(await OrganizeAsync(server.Client, "restore", [firstInTrash]));
        var deleted = Success(await OrganizeAsync(server.Client, "delete", [secondInTrash], confirmDelete: true));
        before[second] = occupied[second];
        Assert.Multiple(() =>
        {
            Assert.That(restored.Files.Single().Destination, Is.EqualTo(first));
            Assert.That(deleted.Files.Single().Destination, Is.Null);
            Assert.That(ReadFiles(root), Is.EqualTo(before));
            Assert.That(SourcePath(element), Is.EqualTo(used));
        });
    }

    [Test]
    public async Task Restore_rolls_back_the_first_move_when_two_entries_have_the_same_destination()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (_, root) = await CreateProjectAsync(server);
        string path = WriteImage(Path.Combine(root, "resources", "same.png"));
        string first = Success(await OrganizeAsync(server.Client, "quarantine", [path])).Files.Single().Destination!;
        WriteImage(path, SKColors.Blue);
        string second = Success(await OrganizeAsync(server.Client, "quarantine", [path])).Files.Single().Destination!;
        var before = ReadFiles(root);

        ToolResult<OrganizeProjectAssetsResponse> result = await OrganizeAsync(server.Client, "restore", [first, second]);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(ReadFiles(root), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task Organizing_a_stale_file_session_refuses_to_write()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, root) = await CreateProjectAsync(server);
        string path = WriteImage(Path.Combine(root, "resources", "unused.png"));
        string projectFile = session.Project.Uri!.LocalPath;
        File.SetLastWriteTimeUtc(projectFile, File.GetLastWriteTimeUtc(projectFile).AddSeconds(10));
        var before = ReadFiles(root);

        ToolResult<OrganizeProjectAssetsResponse> result = await OrganizeAsync(server.Client, "quarantine", [path]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ProjectConflict));
            Assert.That(ReadFiles(root), Is.EqualTo(before));
        });
    }

    private static async Task<(FileEditingSession Session, string Root)> CreateProjectAsync(InProcessAgentToolkitServer server)
    {
        string root = TestWorkspace.CreateDirectory();
        Success(ToolResultReader.Read<CreateProjectResponse>(await server.Client.CallToolAsync("create_project", new Dictionary<string, object?>
        {
            ["path"] = Path.Combine(root, "Assets.bep"),
            ["width"] = 64,
            ["height"] = 64,
            ["frameRate"] = 30,
            ["duration"] = "00:00:03"
        })));
        return ((FileEditingSession)server.Services.GetRequiredService<AgentSessionManager>().RequireSession(), root);
    }

    private static Element AddImage(Scene scene, string name, ImageSource source)
    {
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1), name);
        element.Objects.Add(new SourceImage { Source = { Value = source } });
        return element;
    }

    private static string SourcePath(Element element) => ((SourceImage)element.Objects.Single()).Source.Value!.Uri.LocalPath;

    private static string WriteImage(string path, SKColor? color = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new SKBitmap(4, 4);
        image.Erase(color ?? SKColors.Red);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static Dictionary<string, byte[]> ReadFiles(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

    private static async Task<InspectProjectAssetsResponse> InspectAsync(McpClient client)
        => Success(ToolResultReader.Read<InspectProjectAssetsResponse>(await client.CallToolAsync("inspect_project_assets")));

    private static async Task<ToolResult<OrganizeProjectAssetsResponse>> OrganizeAsync(
        McpClient client, string action, string[] paths, bool confirmDelete = false)
        => ToolResultReader.Read<OrganizeProjectAssetsResponse>(await client.CallToolAsync("organize_project_assets",
            new Dictionary<string, object?> { ["action"] = action, ["paths"] = paths, ["confirmDelete"] = confirmDelete }));

    private static T Success<T>(ToolResult<T> result)
    {
        Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
        return result.Value!;
    }
}
