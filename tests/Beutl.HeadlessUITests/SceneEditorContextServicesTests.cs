using System.Diagnostics.CodeAnalysis;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Guards that SceneEditorExtension.TryCreateContext resolves host services by type through
// IEditorContextServices, so any implementation (including a test fake) is accepted, not only the
// host's concrete EditorContextServices.
[TestFixture]
public class SceneEditorContextServicesTests
{
    // An IEditorContextServices that is deliberately NOT the host's concrete EditorContextServices,
    // so the test exercises the by-type TryGetService path instead of a concrete downcast.
    private sealed class FakeEditorContextServices(EditorService editorService, ExtensionProvider extensionProvider)
        : IEditorContextServices
    {
        public IExtensionProvider ExtensionProvider => extensionProvider;

        public bool TryGetService<T>([NotNullWhen(true)] out T? service)
            where T : class
        {
            service = editorService as T ?? extensionProvider as T;
            return service is not null;
        }
    }

    [Test]
    public void EditorContextServices_TryGetService_resolves_by_type()
    {
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);

        Assert.Multiple(() =>
        {
            Assert.That(services.TryGetService<EditorService>(out EditorService? resolvedEditor), Is.True);
            Assert.That(resolvedEditor, Is.SameAs(editorService));

            Assert.That(services.TryGetService<ExtensionProvider>(out ExtensionProvider? resolvedProvider), Is.True);
            Assert.That(resolvedProvider, Is.SameAs(extensionProvider));

            Assert.That(services.TryGetService<IExtensionProvider>(out IExtensionProvider? resolvedInterface), Is.True);
            Assert.That(resolvedInterface, Is.SameAs(extensionProvider));

            Assert.That(services.TryGetService<string>(out string? missing), Is.False);
            Assert.That(missing, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task TryCreateContext_accepts_a_non_concrete_IEditorContextServices()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "trycreatecontext");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "trycreatecontext")
        {
            Uri = new Uri(Path.Combine(workspace, "trycreatecontext.scene"))
        };

        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new FakeEditorContextServices(editorService, extensionProvider);

        bool created = SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context);

        try
        {
            Assert.That(
                created,
                Is.True,
                "TryCreateContext must accept any IEditorContextServices, not only the host's concrete type.");
            Assert.That(context, Is.Not.Null);
            Assert.That(context, Is.InstanceOf<EditViewModel>());
        }
        finally
        {
            if (context is not null)
            {
                await context.DisposeAsync();
            }

            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Context_disposal_before_tab_publication_is_not_published()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "publication-race");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "publication-race")
        {
            Uri = new Uri(Path.Combine(workspace, "publication-race.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);

        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context),
            Is.True);
        var tab = new EditorTabItem(context!);
        await context!.DisposeAsync();

        editorService.AddTabItem(tab);

        Assert.That(editorService.TabItems, Does.Not.Contain(tab));
        await tab.DisposeAsync();
        HeadlessTestHelpers.Settle();
    }

    [AvaloniaTest]
    public async Task EditViewModelPublicationObserverCanWaitForDisposeWithoutDeadlock()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "context-publication-close");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "context-publication-close")
        {
            Uri = new Uri(Path.Combine(workspace, "context-publication-close.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);

        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context),
            Is.True);
        var gate = (IEditorContextPublicationGate)context!;
        Task? close = null;

        bool published = await Task.Run(() => gate.TryPublish(() =>
        {
            close = Task.Run(() => context!.DisposeAsync().AsTask());
            close.GetAwaiter().GetResult();
        })).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(close, Is.Not.Null);
        await close!.WaitAsync(TimeSpan.FromSeconds(5));
        await context!.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(published, Is.False);
        HeadlessTestHelpers.Settle();
    }
}
