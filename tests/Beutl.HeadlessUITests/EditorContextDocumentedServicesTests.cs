using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// docs/extension-authoring/mcp-tools.md tells extensions to resolve these services from IEditorContext.
public class EditorContextDocumentedServicesTests
{
    [AvaloniaTest]
    public async Task The_scene_and_history_that_the_extension_docs_name_resolve_from_the_editor()
    {
        string workspace = TestShell.NewWorkspace("documented-services-" + Guid.NewGuid().ToString("N"));
        var scene = new Scene(320, 180, "documented-services") { Uri = new Uri(Path.Combine(workspace, "main.scene")) };
        var extensions = new ExtensionProvider(TestShell.Configuration.ExtensionConfig);
        using var library = new Beutl.Services.LibraryService();
        using var templates = new Beutl.Editor.Services.ObjectTemplateService(new SceneRenderServices(TestShell.FontManager), Path.Combine(workspace, "templates"));
        var tutorials = new Beutl.Services.Tutorials.TutorialServiceHandler(TestShell.Configuration.TutorialConfig);
        var palettes = new Beutl.Controls.PropertyEditors.ColorPaletteService(null);
        var services = TestShell.CreateEditorServices()
            .Add(() => TestShell.Configuration)
            .Add(() => library)
            .Add(() => templates)
            .Add<Beutl.Services.Tutorials.ITutorialService>(() => tutorials)
            .Add(() => palettes);
        var editor = new EditViewModel(scene, extensions, new EditorService(extensions, services));
        try
        {
            IEditorContext context = editor;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(context.GetService(typeof(Beutl.Configuration.GlobalConfiguration)), Is.SameAs(TestShell.Configuration));
                Assert.That(context.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry)), Is.SameAs(TestShell.Decoders));
                // The editor's fonts are its own fork of the host's, which still resolves the host's fonts.
                var fonts = context.GetService(typeof(Beutl.Media.FontManager)) as Beutl.Media.FontManager;
                Assert.That(fonts, Is.Not.Null.And.Not.SameAs(TestShell.FontManager));
                Assert.That(fonts?.IsRegistered(TestShell.FontManager.DefaultTypeface.FontFamily), Is.True);
                Assert.That(context.GetService(typeof(Beutl.Services.LibraryService)), Is.SameAs(library));
                Assert.That(context.GetService(typeof(Beutl.Editor.Services.ObjectTemplateService)), Is.SameAs(templates));
                Assert.That(context.GetService(typeof(Beutl.Services.Tutorials.ITutorialService)), Is.SameAs(tutorials));
                Assert.That(context.GetService(typeof(Beutl.Controls.PropertyEditors.ColorPaletteService)), Is.SameAs(palettes));
                Assert.That(context.GetService(typeof(Scene)), Is.SameAs(scene));
                Assert.That(context.GetService(typeof(HistoryManager)), Is.SameAs(editor.HistoryManager).And.Not.Null);
            }
        }
        finally
        {
            await editor.DisposeAsync();
        }
    }
}
