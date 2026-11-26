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

public sealed class MissingMediaToolsTests
{
    [Test]
    public async Task Stdio_exposes_the_repair_schema_and_routes_it_to_live_scenes()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        IList<McpClientTool> tools = await server.Client.ListToolsAsync();
        var scan = tools.Single(tool => tool.Name == "scan_missing_media").JsonSchema.GetProperty("properties");
        var repair = tools.Single(tool => tool.Name == "relink_missing_media").JsonSchema.GetProperty("properties");
        var pair = repair.GetProperty("replacements").GetProperty("items").GetProperty("properties");
        Assert.Multiple(() =>
        {
            Assert.That(scan.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "sceneId", "instanceId" }));
            Assert.That(repair.EnumerateObject().Select(property => property.Name),
                Is.EquivalentTo(new[] { "replacements", "searchFolder", "sceneId", "instanceId" }));
            Assert.That(pair.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "missingPath", "newPath" }));
            Assert.That(tools.Select(tool => tool.Name), Does.Not.Contain("generate_proxies"));
        });
    }

    [Test]
    public async Task Scan_finds_all_references_and_pair_repair_saves_valid_replacements_in_one_batch()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, directory) = await CreateProjectAsync(server);
        string original = Path.Combine(directory, "missing.png");
        string stillMissing = Path.Combine(directory, "offline.png");
        Element first = AddImage(session.Scene, original, 0);
        Element second = AddImage(session.Scene, original, 1);
        second.IsLocked.Value = true;
        AddImage(session.Scene, stillMissing, 2);
        session.History.Commit("Add clips");
        session.Save(skipConflictCheck: false);
        int historyCount = session.History.UndoCount;
        string replacement = WriteImage(directory, "replacement.png", SKColors.Red);

        var missing = await CallAsync<ScanMissingMediaResponse>(server.Client, "scan_missing_media");
        MissingMediaItem image = missing.Missing.Single(item => item.OriginalPath == original);
        Assert.Multiple(() =>
        {
            Assert.That(image.Kind, Is.EqualTo("image"));
            Assert.That(image.ElementIds, Is.EquivalentTo(new[] { first.Id.Value.ToString(), second.Id.Value.ToString() }));
        });

        var repaired = await CallAsync<RelinkMissingMediaResponse>(server.Client, "relink_missing_media", new()
        {
            ["replacements"] = new[] { new MediaRelinkPair(original, replacement), new MediaRelinkPair(stillMissing, Path.Combine(directory, "absent.png")) }
        });
        Assert.Multiple(() =>
        {
            Assert.That(repaired.Relinked.Single().Media.ElementIds, Is.EquivalentTo(image.ElementIds));
            Assert.That(repaired.Relinked.Single().NewPath, Is.EqualTo(replacement));
            Assert.That(repaired.RemainingMissing.Single().OriginalPath, Is.EqualTo(stillMissing));
            Assert.That(repaired.Issues.Single().OriginalPath, Is.EqualTo(stillMissing));
            Assert.That(session.History.UndoCount, Is.EqualTo(historyCount));
            Assert.That(second.IsLocked.Value, Is.True);
        });

        Scene reopened = CoreSerializer.RestoreFromUri<Project>(session.Project.Uri!).Items.OfType<Scene>().Single();
        Assert.That(reopened.Children.Where(element => element.Id.Value == first.Id.Value || element.Id.Value == second.Id.Value)
            .Select(SourcePath), Is.EqualTo(new[] { replacement, replacement }));
    }

    [Test]
    public async Task Folder_repair_matches_renamed_media_by_fingerprint_and_leaves_ambiguous_names_missing()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, directory) = await CreateProjectAsync(server);
        string original = WriteImage(directory, "original.png", SKColors.Green);
        string ambiguous = Path.Combine(directory, "ambiguous.png");
        Element clip = AddImage(session.Scene, original, 0);
        AddImage(session.Scene, ambiguous, 1);
        var detector = new MissingMediaService(session.Fonts, server.Services.GetRequiredService<DecoderRegistry>());
        await detector.UpdateFingerprintsAsync(session.Scene);
        session.Save(skipConflictCheck: false);
        string folder = Directory.CreateDirectory(Path.Combine(directory, "search")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
        string renamed = Path.Combine(nested, "renamed.png");
        File.Move(original, renamed);
        WriteImage(folder, "ambiguous.png", SKColors.Red);
        WriteImage(nested, "ambiguous.png", SKColors.Blue);

        var repaired = await CallAsync<RelinkMissingMediaResponse>(server.Client, "relink_missing_media", new() { ["searchFolder"] = folder });

        Assert.Multiple(() =>
        {
            Assert.That(repaired.Relinked.Single().NewPath, Is.EqualTo(renamed));
            Assert.That(SourcePath(clip), Is.EqualTo(renamed));
            Assert.That(repaired.RemainingMissing.Single().OriginalPath, Is.EqualTo(ambiguous));
        });
    }

    [Test]
    public async Task Save_conflict_restores_the_original_source_and_leaves_the_session_repairable()
    {
        await using var server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        var (session, directory) = await CreateProjectAsync(server);
        string original = Path.Combine(directory, "missing.png");
        Element clip = AddImage(session.Scene, original, 0);
        session.History.Commit("Add clip");
        session.Save(skipConflictCheck: false);
        string file = session.Project.Uri!.LocalPath;
        DateTime stamp = File.GetLastWriteTimeUtc(file);
        File.SetLastWriteTimeUtc(file, stamp.AddSeconds(10));
        string replacement = WriteImage(directory, "replacement.png", SKColors.Red);
        var arguments = new Dictionary<string, object?> { ["replacements"] = new[] { new MediaRelinkPair(original, replacement) } };

        var rejected = ToolResultReader.Read<RelinkMissingMediaResponse>(await server.Client.CallToolAsync("relink_missing_media", arguments));
        Assert.That(rejected.Error?.Code, Is.EqualTo(ErrorCode.ProjectConflict));
        Assert.That(SourcePath(clip), Is.EqualTo(original));

        File.SetLastWriteTimeUtc(file, stamp);
        var repaired = await CallAsync<RelinkMissingMediaResponse>(server.Client, "relink_missing_media", arguments);
        Assert.That(repaired.RemainingMissing, Is.Empty);
        Assert.That(SourcePath(clip), Is.EqualTo(replacement));
    }

    private static async Task<(FileEditingSession Session, string Directory)> CreateProjectAsync(InProcessAgentToolkitServer server)
    {
        string directory = TestWorkspace.CreateDirectory();
        await CallAsync<CreateProjectResponse>(server.Client, "create_project", new()
        {
            ["path"] = Path.Combine(directory, "Repair.bep"),
            ["width"] = 640,
            ["height"] = 480,
            ["frameRate"] = 30,
            ["duration"] = "00:00:03"
        });
        return ((FileEditingSession)server.Services.GetRequiredService<AgentSessionManager>().RequireSession(), directory);
    }

    private static Element AddImage(Scene scene, string path, int layer)
    {
        var element = new Element { Length = { Value = MediaTime.FromSeconds(1) }, ZIndex = { Value = layer } };
        element.Objects.Add(new SourceImage { Source = { Value = ImageSource.Open(path) } });
        scene.Children.Add(element);
        return element;
    }

    private static string SourcePath(Element element) => ((SourceImage)element.Objects.Single()).Source.Value!.Uri.LocalPath;

    private static string WriteImage(string directory, string name, SKColor color)
    {
        using var image = new SKBitmap(8, 8);
        image.Erase(color);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, png.ToArray());
        return path;
    }

    private static async Task<T> CallAsync<T>(McpClient client, string name, Dictionary<string, object?>? arguments = null)
    {
        ToolResult<T> result = ToolResultReader.Read<T>(await client.CallToolAsync(name, arguments));
        Assert.That(result.IsSuccess, Is.True, result.Error?.ToString());
        return result.Value!;
    }
}
