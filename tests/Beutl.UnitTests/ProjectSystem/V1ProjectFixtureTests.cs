using System.Collections;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.IO;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.UnitTests.ProjectSystem;

// Opens a project that Beutl 1.1.0 saved, the way the app opens a project, and compares what 2.x makes of
// it with Assets/V1ProjectFixture.approved.txt: the objects each element migrates to and their values that
// differ from a new object of the same type, the 1.1.0 values 2.x does not read, which elements recovery
// keeps read-only, and the notices shown on open. A failure says where it wrote the result and how to approve it.
//
// Assets/V1Project holds the files exactly as v1.1.0 wrote them. A throwaway program built against the
// v1.1.0 tag produced them: it followed that version's ProjectService.CreateProject and
// EditViewModel.AddElement and saved with its own serializer. There is one element per operator type
// v1.1.0 offers (EllipseOperator, RectOperator, RoundedRectOperator, GeometryOperator, TextBlockOperator,
// SourceVideoOperator, SourceImageOperator, SourceBackdropOperator, SourceSoundOperator, DecorateOperator
// and GroupOperator), with the TakeAfterOperator that DecorateOperator and GroupOperator need, and one
// element drawn by its node tree. The media paths point to files that are not shipped.
[TestFixture]
public class V1ProjectFixtureTests
{
    private const string FixtureResourcePrefix = "Beutl.UnitTests.Assets.V1Project.";
    private const string ApprovedResourceName = "Beutl.UnitTests.Assets.V1ProjectFixture.approved.txt";
    private const string ProjectFileName = "V1Project.bep";
    private const string SceneDirectoryName = "V1Project";

    private static readonly JsonSerializerOptions s_valueOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The "[Assembly]Namespace:" parts of a type name, so "[Beutl.Engine]Beutl.Animation:KeyFrame<
    // [System.Private.CoreLib]System:Single>" reads "KeyFrame<Single>".
    private static readonly Regex s_typeNameQualifier = new(@"\[[^\]]*\][^:<>,\[\]]*:", RegexOptions.CultureInvariant);

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-v1-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    [SetCulture("en-US")]
    [SetUICulture("en-US")]
    public void Opening_a_project_saved_by_1_1_0_gives_the_approved_result()
    {
        string projectPath = ExtractFixture();

        Project project = CoreSerializer.RestoreFromUri<Project>(UriHelper.CreateFromPath(projectPath));
        string actual = Describe(project);

        string approved = ReadResource(ApprovedResourceName).ReplaceLineEndings("\n");
        if (actual == approved) return;

        string received = Path.Combine(TestContext.CurrentContext.WorkDirectory, "V1ProjectFixture.received.txt");
        File.WriteAllText(received, actual);
        TestContext.AddTestAttachment(received);
        Assert.Fail(
            $"Opening the 1.1.0 project no longer gives the approved result. {FirstDifference(approved, actual)}\n"
            + $"The result was written to {received}. If the change is intended, copy it over "
            + "tests/Beutl.UnitTests/Assets/V1ProjectFixture.approved.txt.");
    }

    private string ExtractFixture()
    {
        var assembly = typeof(V1ProjectFixtureTests).Assembly;
        string[] names = assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(FixtureResourcePrefix, StringComparison.Ordinal))
            .ToArray();
        Assert.That(names, Is.Not.Empty);

        foreach (string name in names)
        {
            // Resource names flatten the folders: "V1Project.bep" is the project, and every other file is in
            // the scene folder, "V1Project/".
            string relative = name[FixtureResourcePrefix.Length..];
            string path = relative == ProjectFileName
                ? Path.Combine(_root, ProjectFileName)
                : Path.Combine(_root, SceneDirectoryName, relative[(SceneDirectoryName.Length + 1)..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using Stream input = assembly.GetManifestResourceStream(name)!;
            using FileStream output = File.Create(path);
            input.CopyTo(output);
        }

        return Path.Combine(_root, ProjectFileName);
    }

    private static string ReadResource(string name)
    {
        using Stream stream = typeof(V1ProjectFixtureTests).Assembly.GetManifestResourceStream(name)
                              ?? throw new InvalidOperationException($"Missing resource {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Describe(Project project)
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');

        Line($"Project variables: {string.Join(", ", project.Variables.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}"))}");
        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            Line($"Scene \"{scene.Name}\": {scene.FrameSize.Width}x{scene.FrameSize.Height}, duration {scene.Duration}, {scene.Children.Count} elements");
        }

        Line();
        Line("Notices on open:");
        DescribeNotice(Line, "project", UnmigratedNodeTreeNotice.Create(project));
        // 2.x drops what 1.1.0 kept for itself, so the project keeps no data this build does not know.
        DescribeNotice(Line, "project's kept data", KeptUnknownDataNotice.Create(project));
        foreach (Scene scene in project.Items.OfType<Scene>())
        {
            DescribeNotice(Line, $"scene \"{scene.Name}\"", RecoveryProtectionNotice.Create(scene));
        }

        foreach (Element element in project.Items.OfType<Scene>().SelectMany(s => s.Children).OrderBy(e => e.ZIndex))
        {
            Line();
            DescribeElement(Line, element);
        }

        return text.ToString();
    }

    private static void DescribeNotice(Action<string> line, string where, Notification? notice)
    {
        if (notice is null)
        {
            line($"  none for the {where}");
            return;
        }

        line($"  {notice.Type} for the {where}: {notice.Title}");
        foreach (string messageLine in notice.Message.ReplaceLineEndings("\n").Split('\n'))
        {
            line($"    {messageLine}".TrimEnd());
        }
    }

    private static void DescribeElement(Action<string> line, Element element)
    {
        string file = Path.GetFileName(element.Uri!.LocalPath);
        line($"Layer {element.ZIndex}: \"{element.Name}\" ({file})");
        line($"  start {element.Start}, length {element.Length}, enabled {element.IsEnabled}, accent color {element.AccentColor}");

        // What 1.1.0 wrote for the element, read from its file.
        JsonObject written = JsonNode.Parse(File.ReadAllText(element.Uri.LocalPath))!.AsObject();
        JsonObject[] operators = written["Operation"]?["Children"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        string operatorList = string.Join(", ", operators.Select(static op =>
            ShortTypeName((string?)op["$type"]) + ((bool?)op["IsEnabled"] == false ? " (turned off)" : string.Empty)));
        int nodes = written["NodeTree"]?["Nodes"]?.AsArray().Count ?? 0;
        line($"  1.1.0 wrote: operators [{operatorList}], UseNode {(bool?)written["UseNode"]}, {nodes} nodes");

        if (element.SuppressedStorageSource is { } kept)
        {
            line("  read-only: recovery keeps the file as 1.1.0 wrote it");
            if (kept.HasNonFallbackIncidents) line("    with load incidents that hold no fallback");
            foreach (SuppressedRecoveryIncident incident in kept.RecoveryIncidents ?? [])
            {
                line($"    incident: {incident.Reason} {incident.TypeName}: {incident.Message}");
            }
        }
        else
        {
            line("  editable: recovery does not keep the file");
        }

        // The migrated objects follow the operators in order, after the node tree when one was kept.
        int first = element.Objects.Count > 0 && ElementMigration.IsUnmigratedNodeTree(element.Objects[0]) ? 1 : 0;
        for (int i = 0; i < element.Objects.Count; i++)
        {
            JsonObject? writtenOperator = i >= first && i - first < operators.Length ? operators[i - first] : null;
            DescribeObject(line, i, element.Objects[i], writtenOperator);
        }
    }

    private static void DescribeObject(Action<string> line, int index, EngineObject obj, JsonObject? writtenOperator)
    {
        if (obj is IFallback fallback)
        {
            fallback.TryGetTypeName(out string? typeName);
            string error = fallback.ErrorMessage is { } message ? $": {message}" : string.Empty;
            line($"  [{index}] {obj.GetType().Name} for {typeName} ({fallback.Reason}{error})");
            if (fallback.Json?["Nodes"] is JsonArray nodes)
            {
                line($"      nodes: {string.Join(", ", nodes.Select(static n => ShortTypeName((string?)n?["$type"])))}");
            }

            return;
        }

        line($"  [{index}] {ShortTypeName(TypeFormat.ToString(obj.GetType()))}");
        if ((bool?)writtenOperator?["IsEnabled"] == false && obj.IsEnabled)
        {
            // Pinned as it is: ElementMigration reads an operator's Value, not the operator's own
            // IsEnabled. 1.1.0 skipped a turned-off DecorateOperator or GroupOperator; 2.x applies it.
            line("      KNOWN GAP: 1.1.0 wrote this operator turned off, but the migrated object is enabled");
        }

        JsonObject actual = CoreSerializer.SerializeToJsonObject(obj);
        if (writtenOperator?["Value"] is JsonObject writtenValue)
        {
            string[] notRead = writtenValue
                .Select(static property => property.Key)
                .Where(key => key is not ("Id" or "$type") && !actual.ContainsKey(key))
                .ToArray();
            if (notRead.Length > 0) line($"      1.1.0 values not read: {string.Join(", ", notRead)}");
        }

        // The element makes its objects follow its own range and layer, which the element line shows.
        actual.Remove("Start");
        actual.Remove("Duration");
        actual.Remove("ZIndex");

        // The values that differ from a new object of the same type, as 2.x would save them.
        foreach (string difference in Diff(actual, DefaultJson(obj.GetType()), string.Empty, obj))
        {
            line($"      {difference}");
        }
    }

    // Lists the leaves of actual that differ from baseline. An object whose type differs from the
    // baseline's, or that the baseline lacks, is compared with a new object of its own type. live is the
    // object actual was written from; it gives that type for an object written without $type (a sealed
    // type such as Pen).
    private static IEnumerable<string> Diff(JsonNode? actual, JsonNode? baseline, string path, object? live)
    {
        switch (actual)
        {
            case JsonObject actualObject:
                {
                    var baselineObject = baseline as JsonObject;
                    string? typeName = (string?)actualObject["$type"];
                    if (typeName is not null && (string?)baselineObject?["$type"] != typeName)
                    {
                        yield return $"{path}: {ShortTypeName(typeName)}";
                        baselineObject = DefaultJson(TypeFormat.ToType(typeName));
                    }
                    else if (typeName is null && baselineObject is null && live is ICoreSerializable)
                    {
                        yield return $"{path}: {ShortTypeName(TypeFormat.ToString(live.GetType()))}";
                        baselineObject = DefaultJson(live.GetType());
                    }

                    foreach ((string key, JsonNode? value) in actualObject)
                    {
                        if (key is "Id" or "$type") continue;
                        foreach (string difference in Diff(value, baselineObject?[key], Join(path, key), Child(live, key)))
                        {
                            yield return difference;
                        }
                    }

                    break;
                }

            case JsonArray actualArray:
                {
                    var baselineArray = baseline as JsonArray;
                    if (baselineArray?.Count != actualArray.Count)
                    {
                        yield return $"{path}: {actualArray.Count} item(s)";
                    }

                    for (int i = 0; i < actualArray.Count; i++)
                    {
                        JsonNode? baselineItem = baselineArray is not null && i < baselineArray.Count ? baselineArray[i] : null;
                        object? liveItem = live is IList list && i < list.Count ? list[i] : null;
                        foreach (string difference in Diff(actualArray[i], baselineItem, $"{path}[{i}]", liveItem))
                        {
                            yield return difference;
                        }
                    }

                    break;
                }

            default:
                if (!JsonNode.DeepEquals(actual, baseline))
                {
                    yield return $"{path} = {actual?.ToJsonString(s_valueOptions) ?? "null"}";
                }

                break;
        }
    }

    // A new object of the type as 2.x saves it, or null when the type cannot be created.
    private static JsonObject? DefaultJson(Type? type)
    {
        if (type is null
            || type.IsAbstract
            || !type.IsAssignableTo(typeof(ICoreSerializable))
            || type.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        JsonObject json = CoreSerializer.SerializeToJsonObject((ICoreSerializable)Activator.CreateInstance(type)!);
        // A new TextBlock takes the platform's default font (FontFamily.Default), which can be the font the
        // fixture uses. Leaving the font family out of the baseline lists the restored one on every machine.
        json.Remove("FontFamily");
        return json;
    }

    // The value of the property named key on a live object, or null.
    private static object? Child(object? parent, string key)
        => parent is CoreObject coreObject && coreObject.Properties.FirstOrDefault(p => p.Name == key) is { } property
            ? property.Value
            : null;

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static string ShortTypeName(string? typeName)
        => typeName is null ? "(no $type)" : s_typeNameQualifier.Replace(typeName, string.Empty);

    private static string FirstDifference(string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        for (int i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            string? e = i < expectedLines.Length ? expectedLines[i] : null;
            string? a = i < actualLines.Length ? actualLines[i] : null;
            if (e != a)
            {
                return $"First difference at line {i + 1}: expected \"{e}\", got \"{a}\".";
            }
        }

        return string.Empty;
    }
}
