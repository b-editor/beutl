using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class SubtitleToolsTests
{
    private const string Srt = """
        1
        00:00:01,234 --> 00:00:01,800
        Café & tea
        次

        2
        00:00:01,500 --> 00:00:02,456
        Overlap

        3
        00:00:03,500 --> 00:00:03,501
        Fin
        """;

    private const string Ass = """
        [Script Info]
        ScriptType: v4.00+

        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:01.23,0:00:01.80,Default,,0,0,0,,Café & tea\N次
        Dialogue: 0,0:00:01.50,0:00:02.46,Default,,0,0,0,,Overlap
        Dialogue: 0,0:00:03.50,0:00:03.51,Default,,0,0,0,,Fin
        """;

    private const string WebVtt = """
        WEBVTT

        00:00:01.234 --> 00:00:01.800
        Café &amp; tea
        次

        00:00:01.500 --> 00:00:02.456
        Overlap

        00:00:03.500 --> 00:00:03.501
        Fin
        """;

    [Test]
    public async Task Stdio_advertises_subtitle_inputs_and_routes()
    {
        var schemas = new (string Name, string[] Required, string[] Optional, bool ReadOnly)[]
        {
            ("import_subtitles", ["path"], ["templateId", "sceneId", "instanceId"], false),
            ("list_subtitles", [], ["layers", "sceneId", "instanceId"], true),
            ("export_subtitles", ["outputPath"], ["elementIds", "layers", "confirmOverwrite", "sceneId", "instanceId"], false),
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
                Assert.That(tool.ProtocolTool.Annotations?.ReadOnlyHint ?? false, Is.EqualTo(expected.ReadOnly), expected.Name);
            });
        }
    }

    [TestCase("srt")]
    [TestCase("ass")]
    [TestCase("vtt")]
    public async Task File_round_trip_preserves_overlapping_and_short_cues_and_import_is_one_undo_step(string extension)
    {
        string directory = TestWorkspace.CreateDirectory();
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(directory);
        FileEditingSession session = await CreateProjectAsync(server);
        McpClient client = server.Client;
        Element background = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(5));
        session.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        session.History.Commit("Seed");
        int historyBefore = session.History.UndoCount;

        (string Content, ICaptionDecoder Decoder) fixture = extension switch
        {
            "srt" => (Srt, new SrtCaptionCodec()),
            "ass" => (Ass, new AssCaptionCodec()),
            _ => (WebVtt, new WebVttCaptionCodec()),
        };
        string inputPath = Path.Combine(directory, "input." + extension);
        // Exercise the editor's BOM handling as well as UTF-8.
        await File.WriteAllTextAsync(inputPath, fixture.Content, extension == "srt" ? Encoding.Unicode : Encoding.UTF8);
        var imported = Success(await Call<ImportSubtitlesResponse>(client, "import_subtitles", ("path", inputPath)));
        var listed = Success(await Call<ListSubtitlesResponse>(client, "list_subtitles"));
        Assert.Multiple(() =>
        {
            Assert.That(imported.CueCount, Is.EqualTo(3));
            Assert.That(imported.HasSkippedCues, Is.False);
            Assert.That(imported.CreatedElementIds, Is.EquivalentTo(listed.Cues.Select(cue => cue.ElementId)));
            Assert.That(listed.Cues.Select(cue => cue.Layer), Is.EqualTo(new[] { 2, 3, 2 }));
            Assert.That(listed.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "Café & tea\n次", "Overlap", "Fin" }));
            Assert.That(listed.Cues.Select(cue => cue.StartSeconds),
                Is.EqualTo(new[] { extension == "ass" ? 1.23 : 1.234, 1.5, 3.5 }).Within(0.000001));
            Assert.That(listed.Cues.Select(cue => cue.EndSeconds),
                Is.EqualTo(extension == "ass" ? new[] { 1.8, 2.46, 3.51 } : new[] { 1.8, 2.456, 3.501 }).Within(0.000001));
            Assert.That(session.History.UndoCount, Is.EqualTo(historyBefore + 1));
            Assert.That(session.IsDirty, Is.True);
        });

        var filtered = Success(await Call<ListSubtitlesResponse>(client, "list_subtitles", ("layers", new[] { 3 })));
        Assert.That(filtered.Cues.Single(), Is.EqualTo(listed.Cues[1]));

        string outputPath = Path.Combine(directory, "output." + extension);
        var exported = Success(await Call<ExportSubtitlesResponse>(client, "export_subtitles",
            ("outputPath", outputPath), ("elementIds", imported.CreatedElementIds.Append(background.Id.Value.ToString()).ToArray())));
        CaptionDocument document = fixture.Decoder.Decode(await File.ReadAllTextAsync(outputPath)).Document!;
        Assert.Multiple(() =>
        {
            Assert.That(exported.CueCount, Is.EqualTo(3));
            Assert.That(exported.SkippedClipCount, Is.EqualTo(1));
            Assert.That(exported.OutputPath, Is.EqualTo(outputPath));
            Assert.That(document.Cues.Select(cue => cue.Text), Is.EqualTo(listed.Cues.Select(cue => cue.Text)));
            Assert.That(document.Cues.Select(cue => cue.Start.TotalSeconds),
                Is.EqualTo(listed.Cues.Select(cue => cue.StartSeconds)).Within(0.000001));
            Assert.That(document.Cues.Select(cue => cue.End.TotalSeconds),
                Is.EqualTo(listed.Cues.Select(cue => cue.EndSeconds)).Within(0.000001));
            Assert.That(session.History.UndoCount, Is.EqualTo(historyBefore + 1));
        });

        string layerPath = Path.Combine(directory, "layer." + extension);
        exported = Success(await Call<ExportSubtitlesResponse>(client, "export_subtitles",
            ("outputPath", layerPath), ("layers", new[] { 3 })));
        Assert.That(exported.CueCount, Is.EqualTo(1));
        Assert.That(fixture.Decoder.Decode(await File.ReadAllTextAsync(layerPath)).Document!.Cues.Single().Text, Is.EqualTo("Overlap"));

        var undone = Success(await Call<HistoryStateResponse>(client, "undo"));
        Assert.Multiple(() =>
        {
            Assert.That(undone.Applied, Has.Count.EqualTo(1));
            Assert.That(session.Scene.Children.Single(), Is.SameAs(background));
            Assert.That(session.Scene.IsLayerLocked(1), Is.True);
            Assert.That(session.History.UndoCount, Is.EqualTo(historyBefore));
        });
    }

    [TestCase("none")]
    [TestCase("clip")]
    [TestCase("layer")]
    public async Task Listed_cues_are_editable_through_apply_edit_with_the_standard_locks(string lockKind)
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        McpClient client = server.Client;
        Success(await Call<ImportSubtitlesResponse>(client, "import_subtitles", ("path", await WriteSrtAsync(Srt))));
        SubtitleCue original = Success(await Call<ListSubtitlesResponse>(client, "list_subtitles")).Cues[0];
        Element clip = session.Scene.Children.Single(item => item.Id.Value.ToString() == original.ElementId);
        if (lockKind == "clip")
        {
            clip.IsLocked.Value = true;
            session.History.Commit("Lock clip");
        }
        else if (lockKind == "layer")
        {
            session.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = original.Layer }, IsLocked = { Value = true } });
            session.History.Commit("Lock layer");
        }
        int historyBefore = session.History.UndoCount;

        var read = Success(await Call<ReadDocumentResponse>(client, "read_document", ("rootId", original.ElementId)));
        string textId = read.Document["Objects"]!.AsArray().Single()!["Id"]!.GetValue<string>();
        var patch = new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = original.ElementId,
                ["Start"] = "00:00:01.250",
                ["Length"] = "00:00:00.750",
                ["Objects"] = new JsonArray(new JsonObject { ["Id"] = textId, ["Text"] = "Edited subtitle" })
            }),
        };
        var edit = await Call<ApplyEditResponse>(client, "apply_edit", ("patch", patch), ("schemaVersion", "1"));
        SubtitleCue updated = Success(await Call<ListSubtitlesResponse>(client, "list_subtitles")).Cues[0];
        if (lockKind == "none")
        {
            Assert.Multiple(() =>
            {
                Assert.That(edit.IsSuccess, Is.True, edit.Error?.Message);
                Assert.That(updated, Is.EqualTo(original with { StartSeconds = 1.25, EndSeconds = 2, Text = "Edited subtitle" }));
                Assert.That(session.History.UndoCount, Is.EqualTo(historyBefore + 1));
            });
            Success(await Call<HistoryStateResponse>(client, "undo"));
            Assert.That(Success(await Call<ListSubtitlesResponse>(client, "list_subtitles")).Cues[0], Is.EqualTo(original));
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(edit.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(edit.Error?.Message, Does.Contain("locked"));
                Assert.That(updated, Is.EqualTo(original));
                Assert.That(session.History.UndoCount, Is.EqualTo(historyBefore));
            });
        }
    }

    [Test]
    public async Task Import_uses_saved_element_set_placement_and_grouping()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        McpClient client = server.Client;
        Scene scene = session.Scene;
        Element backdrop = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1), "backdrop.belm");
        Element title = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1), "title.belm");
        title.ZIndex.Value = 1;
        title.AddObject(new TextBlock { Text = { Value = "Template text" }, Size = { Value = 64 } });
        scene.Groups.Add(ImmutableHashSet.Create(backdrop.Id.Value, title.Id.Value));
        session.History.Commit("Seed");
        var template = Success(await Call<ElementTemplateSummary>(client, "save_element_template",
            ("name", "Caption pair"), ("elementIds", new[] { title.Id.Value.ToString(), backdrop.Id.Value.ToString() })));

        var imported = Success(await Call<ImportSubtitlesResponse>(client, "import_subtitles",
            ("path", await WriteSrtAsync(Srt)), ("templateId", template.TemplateId)));
        Element[] copies = scene.Children.Where(element => imported.CreatedElementIds.Contains(element.Id.Value.ToString())).ToArray();
        Element[] firstCue = copies.Where(element => element.Start.Value == MediaTime.FromSeconds(1.234)).OrderBy(element => element.ZIndex.Value).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(imported.CueCount, Is.EqualTo(3));
            Assert.That(copies, Has.Length.EqualTo(6));
            Assert.That(firstCue.Select(element => element.ZIndex.Value), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(firstCue.Select(element => element.Range.End), Is.All.EqualTo(MediaTime.FromSeconds(1.8)));
            Assert.That(scene.Groups.Any(group => group.SetEquals(firstCue.Select(element => element.Id.Value))), Is.True);
            TextBlock text = firstCue[1].Objects.OfType<TextBlock>().Single();
            Assert.That(text.Text.Value, Is.EqualTo("Café & tea\n次"));
            Assert.That(text.Size.Value, Is.EqualTo(64));
        });
        Success(await Call<HistoryStateResponse>(client, "undo"));
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children, Is.EquivalentTo(new[] { backdrop, title }));
            Assert.That(scene.Groups, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Import_reports_bad_cues_and_export_requires_a_source_and_overwrite_confirmation()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        McpClient client = server.Client;
        string invalidCue = "\n\n4\n00:00:05,000 --> 00:00:04,000\nInvalid";
        var imported = Success(await Call<ImportSubtitlesResponse>(client, "import_subtitles", ("path", await WriteSrtAsync(Srt + invalidCue))));
        Assert.That(imported.HasSkippedCues, Is.True);
        Assert.That(imported.CueCount, Is.EqualTo(3));
        var invalidImport = await Call<ImportSubtitlesResponse>(client, "import_subtitles", ("path", await WriteSrtAsync(invalidCue)));
        Assert.That(invalidImport.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That(session.History.UndoCount, Is.EqualTo(1));

        string outputPath = Path.Combine(TestWorkspace.CreateDirectory(), "existing.srt");
        var noSource = await Call<ExportSubtitlesResponse>(client, "export_subtitles", ("outputPath", outputPath));
        Assert.That(noSource.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        var noText = await Call<ExportSubtitlesResponse>(client, "export_subtitles", ("outputPath", outputPath), ("layers", new[] { 9 }));
        Assert.That(noText.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That(File.Exists(outputPath), Is.False);

        await File.WriteAllTextAsync(outputPath, "Existing");
        var overwrite = await Call<ExportSubtitlesResponse>(client, "export_subtitles",
            ("outputPath", outputPath), ("elementIds", imported.CreatedElementIds));
        Assert.That(overwrite.Error?.Code, Is.EqualTo(ErrorCode.DestructiveIntent));
        Assert.That(await File.ReadAllTextAsync(outputPath), Is.EqualTo("Existing"));
        Success(await Call<ExportSubtitlesResponse>(client, "export_subtitles",
            ("outputPath", outputPath), ("elementIds", imported.CreatedElementIds), ("confirmOverwrite", true)));
        Assert.That(new SrtCaptionCodec().Decode(await File.ReadAllTextAsync(outputPath)).Document!.Count, Is.EqualTo(3));
        Assert.That(session.History.UndoCount, Is.EqualTo(1));
    }

    private static async Task<string> WriteSrtAsync(string content)
    {
        string path = Path.Combine(TestWorkspace.CreateDirectory(), "input.srt");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private static async Task<FileEditingSession> CreateProjectAsync(InProcessAgentToolkitServer server)
    {
        Success(await Call<CreateProjectResponse>(server.Client, "create_project",
            ("path", Path.Combine(TestWorkspace.CreateDirectory(), "Subtitles.bep")),
            ("width", 640), ("height", 360), ("frameRate", 30), ("duration", "00:00:10")));
        return (FileEditingSession)server.Services.GetRequiredService<AgentSessionManager>().RequireSession();
    }

    private static async Task<ToolResult<T>> Call<T>(McpClient client, string tool, params (string Name, object? Value)[] args)
        => ToolResultReader.Read<T>(await client.CallToolAsync(tool, args.ToDictionary(arg => arg.Name, arg => arg.Value)));

    private static T Success<T>(ToolResult<T> result)
    {
        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return result.Value!;
    }
}
