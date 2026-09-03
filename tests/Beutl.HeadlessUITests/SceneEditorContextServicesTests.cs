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
// IEditorContextServices, so matching implementations (including a test fake) are accepted, not
// only the host's concrete EditorContextServices. Host-token mismatches are rejected before context
// construction.
[TestFixture]
public class SceneEditorContextServicesTests
{
    // An IEditorContextServices that is deliberately NOT the host's concrete EditorContextServices,
    // so the test exercises the by-type TryGetService path instead of a concrete downcast.
    private sealed class FakeEditorContextServices(
        EditorService editorService,
        ExtensionProvider extensionProvider,
        IEditorContextCloseService? closeService = null)
        : IEditorContextServices
    {
        public IExtensionProvider ExtensionProvider => extensionProvider;

        public IEditorContextCloseService CloseService => closeService ?? editorService;

        public bool TryGetService<T>([NotNullWhen(true)] out T? service)
            where T : class
        {
            service = editorService as T ?? extensionProvider as T;
            return service is not null;
        }
    }

    private sealed class CountingCloseService(IEditorContextCloseService inner) : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken => inner.HostToken;

        public int RequestCount { get; private set; }

        public EditorContextCloseRequest RequestClose(IEditorContext context)
        {
            RequestCount++;
            return inner.RequestClose(context);
        }
    }

    private sealed class MismatchedEditorContextServices(
        EditorService editorService,
        ExtensionProvider extensionProvider,
        IEditorContextCloseService closeService)
        : IEditorContextServices
    {
        public IExtensionProvider ExtensionProvider => extensionProvider;

        public IEditorContextCloseService CloseService => closeService;

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
            Assert.That(services.CloseService, Is.SameAs(editorService));
            Assert.That(services.CloseService.HostToken, Is.SameAs(editorService.HostToken));

            Assert.That(services.TryGetService<ExtensionProvider>(out ExtensionProvider? resolvedProvider), Is.True);
            Assert.That(resolvedProvider, Is.SameAs(extensionProvider));

            Assert.That(services.TryGetService<IExtensionProvider>(out IExtensionProvider? resolvedInterface), Is.True);
            Assert.That(resolvedInterface, Is.SameAs(extensionProvider));

            Assert.That(services.TryGetService<string>(out string? missing), Is.False);
            Assert.That(missing, Is.Null);
        });
    }

    [AvaloniaTest]
    public async Task ContextCloseService_rejects_a_foreign_context()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "foreign-context-close");
        Directory.CreateDirectory(workspace);
        var firstScene = new Scene(640, 480, "first")
        {
            Uri = new Uri(Path.Combine(workspace, "first.scene"))
        };
        var secondScene = new Scene(640, 480, "second")
        {
            Uri = new Uri(Path.Combine(workspace, "second.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        IEditorContextServices services = new EditorContextServices(editorService, extensionProvider);

        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(firstScene, services, out IEditorContext? first),
            Is.True);
        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(secondScene, services, out IEditorContext? second),
            Is.True);

        try
        {
            EditorContextCloseRequest request = first!.CloseService.RequestClose(second!);
            Assert.Multiple(() =>
            {
                Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.NotOwned));
                Assert.That(request.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(((EditViewModel)second!).IsDisposeRequested, Is.False);
            });
        }
        finally
        {
            await first!.DisposeAsync();
            await second!.DisposeAsync();
        }
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
    public void TryCreateContext_rejects_mismatched_close_service_without_creating_context()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "mismatched-close-service");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "mismatched-close-service")
        {
            Uri = new Uri(Path.Combine(workspace, "mismatched-close-service.scene"))
        };

        var extensionProvider = new ExtensionProvider();
        var owner = new EditorService(extensionProvider);
        var foreign = new EditorService(extensionProvider);
        IEditorContextServices services = new MismatchedEditorContextServices(owner, extensionProvider, foreign);

        bool created = SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context);

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.False);
            Assert.That(context, Is.Null);
            Assert.That(owner.TabItems, Is.Empty);
            Assert.That(foreign.TabItems, Is.Empty);
        });

        HeadlessTestHelpers.Settle();
    }

    [AvaloniaTest]
    public void EditViewModel_constructor_is_not_public()
    {
        Assert.That(typeof(EditViewModel).GetConstructors(), Is.Empty);
    }

    [AvaloniaTest]
    public async Task EditViewModel_constructor_accepts_services_from_same_host()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "edit-view-model-host-match");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "edit-view-model-host-match")
        {
            Uri = new Uri(Path.Combine(workspace, "edit-view-model-host-match.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);

        var viewModel = new EditViewModel(scene, editorService, editorService);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.EditorService, Is.SameAs(editorService));
                Assert.That(viewModel.CloseService.HostToken, Is.SameAs(editorService.HostToken));
                Assert.That(viewModel.ExtensionProvider, Is.SameAs(extensionProvider));
            });
        }
        finally
        {
            await viewModel.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task TryCreateContext_forwards_close_requests_to_supplied_close_service()
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, "supplied-close-service");
        Directory.CreateDirectory(workspace);
        var scene = new Scene(640, 480, "supplied-close-service")
        {
            Uri = new Uri(Path.Combine(workspace, "supplied-close-service.scene"))
        };
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        var suppliedCloseService = new CountingCloseService(editorService);
        IEditorContextServices services = new FakeEditorContextServices(
            editorService,
            extensionProvider,
            suppliedCloseService);

        Assert.That(
            SceneEditorExtension.Instance.TryCreateContext(scene, services, out IEditorContext? context),
            Is.True);
        var tab = new EditorTabItem(context!);
        editorService.AddTabItem(tab);

        try
        {
            Assert.That(context!.CloseService.HostToken, Is.SameAs(suppliedCloseService.HostToken));

            EditorContextCloseRequest request = context.CloseService.RequestClose(context);

            Assert.That(suppliedCloseService.RequestCount, Is.EqualTo(1));
            Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
            await request.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(editorService.TabItems, Is.Empty);
        }
        finally
        {
            await context!.DisposeAsync();
            await tab.DisposeAsync();
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
        Assert.That(editor.CloseService.HostToken, Is.SameAs(services.CloseService.HostToken));
        var tab = new EditorTabItem(editor);
        var beforeDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.BeforePreOwnershipCloseStart = () =>
        {
            beforeDispose.TrySetResult();
            releaseDispose.Task.GetAwaiter().GetResult();
        };
        IEditorContextCloseService closeService = editor.CloseService;
        Assert.That(editor.GetService(typeof(EditorService)), Is.Null);
        Assert.That(editor.GetService(typeof(IEditorContextCloseService)), Is.Null);
        Assert.That(editor.CloseService, Is.SameAs(closeService));

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
                    closeRequest = context!.CloseService.RequestClose(context);
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
        EditorContextCloseRequest closeRequest = default;
        using IDisposable observer = tab.IsSelected.Subscribe(selected =>
        {
            if (selected)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    closeRequest = context!.CloseService.RequestClose(context);
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
