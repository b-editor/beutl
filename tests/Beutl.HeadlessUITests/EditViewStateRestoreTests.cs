using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public class EditViewStateRestoreTests
{
    [AvaloniaTest]
    [TestCase("{", null)]
    [TestCase("[]", null)]
    [TestCase("null", null)]
    [TestCase("{}", typeof(InvalidOperationException))]
    [TestCase("{}", typeof(OperationCanceledException))]
    public async Task A_layout_that_cannot_be_restored_opens_the_scene_and_is_replaced_on_close(
        string content, Type? failure)
    {
        string stateFile = await OpenAndClose(content, failure);

        Assert.That(JsonNode.Parse(File.ReadAllText(stateFile))?["scale"], Is.Not.Null);
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(stateFile)!, "*.corrupt-*"), Is.Empty);
    }

    [AvaloniaTest]
    public async Task A_layout_file_that_cannot_be_read_is_not_replaced_in_that_session()
    {
        const string Content = """{"scale":2}""";
        string stateFile = await OpenAndClose(Content, failure: null, lockWhileOpening: true);

        Assert.That(File.ReadAllText(stateFile), Is.EqualTo(Content));
    }

    // Opens a scene whose layout file holds content, with a default tool tab that throws failure, and closes it.
    private static async Task<string> OpenAndClose(string content, Type? failure, bool lockWhileOpening = false)
    {
        string workspace = TestShell.NewWorkspace("view-state-" + Guid.NewGuid().ToString("N"));
        var scene = new Scene(320, 180, "view-state") { Uri = new Uri(Path.Combine(workspace, "main.scene")) };
        string stateFile = Path.Combine(EditorConstants.GetViewStateDirectory(scene.Uri), "main.config");
        File.WriteAllText(stateFile, content);
        var extensions = new ExtensionProvider();
        if (failure != null)
            extensions.AddExtensions(1, [new FailingToolTabExtension((Exception)Activator.CreateInstance(failure)!)]);

        EditViewModel editor;
        using (lockWhileOpening ? new FileStream(stateFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null)
            editor = new EditViewModel(scene, extensions, new EditorService(extensions));
        await editor.DisposeAsync();
        return stateFile;
    }

    private sealed class FailingToolTabExtension(Exception failure) : ToolTabExtension
    {
        public override string Name => "Failing test tool";

        public override string DisplayName => "Failing test tool";

        public override bool CanMultiple => false;

        public override bool OpenByDefault => true;

        public override bool TryCreateContent(
            IEditorContext editorContext, [NotNullWhen(true)] out Control? control) => throw failure;

        public override bool TryCreateContext(
            IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context) => throw failure;
    }
}
