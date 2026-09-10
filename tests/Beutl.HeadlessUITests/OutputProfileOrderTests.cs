using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class OutputProfileOrderTests
{
    [AvaloniaTest]
    public async Task InterleavedUnavailableProfiles_RetainOrderAndRecoveredDefault()
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "output-order");
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(320, 180, 30, 44100, "output-order", directory))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        const int packageId = 1938201;
        var extension = new OrderOutputExtension();
        editor.ExtensionProvider.AddExtensions(packageId, [extension]);
        string path = Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, EditorConstants.BeutlFolder, "output-profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var template = new OutputProfileItem(new OrderOutputContext(scene, extension), editor, TestShell.Editor);
        string available = OutputProfileItem.ToJson(template)["Extension"]!.GetValue<string>();
        JsonObject Profile(string name, bool known) => new()
        {
            ["Extension"] = known ? available : "[Missing.Plugin]Missing:Output",
            ["File"] = scene.Uri.LocalPath,
            ["Context"] = new JsonObject { ["Name"] = name },
        };
        try
        {
            var original = new JsonArray(Profile("first", false), Profile("second", true), Profile("third", false), Profile("fourth", true));
            File.WriteAllText(path, original.ToJsonString());
            using var service = new OutputService(editor);
            service.RestoreItems();
            Assert.That(service.Items, Has.Count.EqualTo(2));
            service.SaveItems();
            JsonArray saved = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
            Assert.That(saved.Select(item => item!["Context"]!["Name"]!.GetValue<string>()),
                Is.EqualTo(new[] { "first", "second", "third", "fourth" }));
            foreach (JsonNode? item in saved) item!["Extension"] = available;
            File.WriteAllText(path, saved.ToJsonString());
            service.RestoreItems();
            Assert.That(service.Items.First().Context.Name.Value, Is.EqualTo("first"));
        }
        finally
        {
            editor.ExtensionProvider.RemoveExtensions(packageId);
        }
    }

    private sealed class OrderOutputExtension : OutputExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("Test output");
        public override bool IsSupported(Type type) => true;
        public override bool TryCreateControl(IEditorContext editor, IOutputContext context, IOutputExecutionController execution,
            [NotNullWhen(true)] out Control? control) { control = new Border(); return true; }
        public override bool TryCreateContext(IEditorContext editor, [NotNullWhen(true)] out IOutputContext? context)
        {
            context = new OrderOutputContext(editor.Object, this);
            return true;
        }
    }

    private sealed class OrderOutputContext(CoreObject value, OutputExtension extension) : IOutputContext
    {
        public OutputExtension Extension => extension;
        public CoreObject Object => value;
        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>("");
        public IReadOnlyReactiveProperty<bool> IsIndeterminate { get; } = new ReactivePropertySlim<bool>();
        public IReadOnlyReactiveProperty<double> Progress { get; } = new ReactivePropertySlim<double>();
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void ReadFromJson(JsonObject json) => Name.Value = json["Name"]!.GetValue<string>();
        public void WriteToJson(JsonObject json) => json["Name"] = Name.Value;
        public void Dispose() => Name.Dispose();
    }
}
