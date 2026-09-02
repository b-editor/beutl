using System.Diagnostics.CodeAnalysis;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
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

            Assert.That(
                services.TryGetService<IEditorContextCloseService>(out IEditorContextCloseService? closeService),
                Is.True);
            Assert.That(closeService, Is.SameAs(editorService));

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
    public async Task AttachedContextDisposalRemovesRegisteredTab()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "registered-context-close");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "registered-context-close")
        {
            Uri = new Uri(Path.Combine(workspace, "registered-context-close.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);

        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context),
            Is.True);
        var tab = new EditorTabItem(context!);
        editorService.AddTabItem(tab);

        await context!.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(editorService.TabItems, Is.Empty);
            Assert.That(editorService.SelectedTabItem.Value, Is.Null);
        });
        HeadlessTestHelpers.Settle();
    }

    [AvaloniaTest]
    public async Task PreOwnershipCloseRechecksAConcurrentInitialClaim()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "preownership-close-claim");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "preownership-close-claim")
        {
            Uri = new Uri(Path.Combine(workspace, "preownership-close-claim.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);
        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context),
            Is.True);
        var editor = (EditViewModel)context!;
        var tab = new EditorTabItem(editor);
        var beforeDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.BeforePreOwnershipCloseStart = () =>
        {
            beforeDispose.TrySetResult();
            releaseDispose.Task.GetAwaiter().GetResult();
        };
        var closeService = (IEditorContextCloseService)editor.GetService(
            typeof(IEditorContextCloseService))!;

        Task<EditorContextCloseRequest> close = Task.Run(() => closeService.RequestClose(editor));
        await beforeDispose.Task.WaitAsync(TimeSpan.FromSeconds(5));
        editorService.AddTabItem(tab);
        releaseDispose.TrySetResult();

        EditorContextCloseRequest request = await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(
            request.Status,
            Is.AnyOf(EditorContextCloseRequestStatus.Accepted, EditorContextCloseRequestStatus.AlreadyClosing));
        await request.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(editorService.TabItems, Is.Empty);
        HeadlessTestHelpers.Settle();
    }

    [AvaloniaTest]
    public async Task EditViewModelPublicationObserverCanRequestCloseAcrossDispatcher()
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
        var tab = new EditorTabItem(context!);
        editorService.AddTabItem(tab);
        var gate = (IEditorContextPublicationGate)context!;
        Assert.That(
            services.TryGetService<IEditorContextCloseService>(out IEditorContextCloseService? closeService),
            Is.True);
        EditorContextCloseRequest closeRequest = default;
        bool requestReturned = false;
        bool completionWasPending = false;
        Task<bool> publication;
        using (ExecutionContext.SuppressFlow())
        {
            publication = Task.Run(() => gate.TryPublish(() =>
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    closeRequest = closeService!.RequestClose(context!);
                    requestReturned = true;
                    completionWasPending = !closeRequest.Completion.IsCompleted;
                });
            }));
        }

        bool published = await publication.WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(requestReturned, Is.True);
            Assert.That(completionWasPending, Is.True);
            Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(published, Is.False);
            Assert.That(editorService.TabItems, Is.Empty);
        });
        HeadlessTestHelpers.Settle();
    }

    [AvaloniaTest]
    public async Task TabSelectionObserverCanDispatchCloseWithoutExecutionContext()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "tab-publication-dispatch-close");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "tab-publication-dispatch-close")
        {
            Uri = new Uri(Path.Combine(workspace, "tab-publication-dispatch-close.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);

        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context),
            Is.True);
        var tab = new EditorTabItem(context!);
        editorService.AddTabItem(tab);
        Assert.That(
            services.TryGetService<IEditorContextCloseService>(out IEditorContextCloseService? closeService),
            Is.True);
        EditorContextCloseRequest closeRequest = default;
        using IDisposable observer = tab.IsSelected.Subscribe(selected =>
        {
            if (selected)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    closeRequest = closeService!.RequestClose(context!);
                });
            }
        });

        Task activation;
        using (ExecutionContext.SuppressFlow())
            activation = Task.Run(() => editorService.ActivateTabItem(scene));

        await activation.WaitAsync(TimeSpan.FromSeconds(5));
        await closeRequest.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(closeRequest.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            Assert.That(editorService.TabItems, Is.Empty);
            Assert.That(editorService.SelectedTabItem.Value, Is.Null);
        });
        HeadlessTestHelpers.Settle();
    }
}
