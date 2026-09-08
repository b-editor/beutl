using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;

using Beutl.Controls;
using Beutl.Editor.Components.WebBrowserTab;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.ViewModels.Dock;

using Dock.Model.Controls;
using Dock.Model.Core;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class WebBrowserTabLifecycleTests
{
    [AvaloniaTest]
    public void Dispose_RemovesTheHostedNativeWebView()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false));
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);

        view.DataContext = viewModel;
        ContentControl host = view.FindControl<ContentControl>("WebViewHost")!;
        TextBox addressTextBox = view.FindControl<TextBox>("AddressTextBox")!;
        Button menuButton = view.FindControl<Button>("BrowserMenuButton")!;

        Assert.Multiple(() =>
        {
            Assert.That(host.Content, Is.SameAs(nativeWebView));
            Assert.That(host.HorizontalContentAlignment, Is.EqualTo(HorizontalAlignment.Stretch));
            Assert.That(host.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Stretch));
            Assert.That(TextBoxAttachment.GetEnterDownBehavior(addressTextBox),
                Is.EqualTo(TextBoxAttachment.EnterBehaviorMode.None));
            Assert.That(menuButton.Flyout, Is.TypeOf<FAMenuFlyout>());
        });

        view.Dispose();

        Assert.That(host.Content, Is.Null);
    }

    [AvaloniaTest]
    public void LinuxSizeRefresh_TogglesVisibilityAndRestoresItsValue()
    {
        var nativeWebView = new NativeWebView { IsVisible = true };
        var visibilityChanges = new List<bool>();
        using IDisposable subscription = nativeWebView
            .GetObservable(Visual.IsVisibleProperty)
            .Subscribe(visibilityChanges.Add);

        WebBrowserTabView.RefreshWebViewBounds(nativeWebView);

        Assert.Multiple(() =>
        {
            Assert.That(nativeWebView.IsVisible, Is.True);
            Assert.That(visibilityChanges, Does.Contain(false));
            Assert.That(visibilityChanges[^1], Is.True);
        });
    }

    [AvaloniaTest]
    public void Rebinding_ReusesTheNativeWebViewAndNavigatesToTheNewContext()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false));
        var context = new TestEditorContext();
        using var first = new WebBrowserTabViewModel(context);
        using var second = new WebBrowserTabViewModel(context);
        var secondUri = new Uri("https://example.com/second");
        second.BeginNavigation(secondUri);

        view.DataContext = first;
        view.DataContext = null;
        view.DataContext = second;

        ContentControl host = view.FindControl<ContentControl>("WebViewHost")!;
        Assert.Multiple(() =>
        {
            Assert.That(host.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebView.Source, Is.EqualTo(secondUri));
        });

        view.Dispose();
    }

    [AvaloniaTest]
    public void ReparentingWithoutANativeAdapter_LeavesTheWebViewAttached()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false));
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);
        view.DataContext = viewModel;

        IDisposable? reparentingScope = ((IWebViewReparentingContent)view).BeginReparenting();

        Assert.Multiple(() =>
        {
            Assert.That(reparentingScope, Is.Null);
            Assert.That(view.FindControl<ContentControl>("WebViewHost")!.Content, Is.SameAs(nativeWebView));
        });
        view.Dispose();
    }

    [AvaloniaTest]
    public void FloatingBrowserDockable_DetachesNativeWebViewDuringDockMove()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false),
            canReparentWebView: _ => true);
        var context = new TestEditorContext();
        var viewModel = new WebBrowserTabViewModel(context);
        using var dockable = new BeutlToolDockable(viewModel, null!)
        {
            ToolContent = view
        };
        view.DataContext = viewModel;

        ContentControl webViewHost = view.FindControl<ContentControl>("WebViewHost")!;
        bool nativeWebViewWasDetached = false;
        using IDisposable subscription = webViewHost
            .GetObservable(ContentControl.ContentProperty)
            .Subscribe(content => nativeWebViewWasDetached |= content == null);

        var factory = new BeutlDockFactory(null!);
        IToolDock toolDock = factory.CreateToolDock();
        toolDock.IsCollapsable = false;
        toolDock.VisibleDockables = factory.CreateList<IDockable>(dockable);
        toolDock.ActiveDockable = dockable;
        IRootDock rootDock = factory.CreateRootDock();
        rootDock.VisibleDockables = factory.CreateList<IDockable>(toolDock);
        rootDock.ActiveDockable = toolDock;
        factory.InitDockable(rootDock, null);

        factory.FloatDockable(dockable);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(rootDock.Windows, Has.Count.EqualTo(1));
        });

        IDock floatingDock = (IDock)dockable.Owner!;
        nativeWebViewWasDetached = false;

        factory.MoveDockable(floatingDock, toolDock, dockable, null);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(dockable.Owner, Is.SameAs(toolDock));
        });

        nativeWebViewWasDetached = false;

        factory.FloatAllDockables(dockable);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(dockable.Owner, Is.Not.SameAs(toolDock));
        });

        IDock secondFloatingDock = (IDock)dockable.Owner!;
        ITool swapTarget = factory.CreateTool();
        factory.AddDockable(toolDock, swapTarget);
        nativeWebViewWasDetached = false;

        factory.SwapDockable(secondFloatingDock, toolDock, dockable, swapTarget);

        Assert.Multiple(() =>
        {
            Assert.That(webViewHost.Content, Is.SameAs(nativeWebView));
            Assert.That(nativeWebViewWasDetached, Is.True);
            Assert.That(dockable.Owner, Is.SameAs(toolDock));
        });
    }

    [AvaloniaTest]
    public async Task CompletedNavigation_UsesTheDocumentTitleForTheToolHeader()
    {
        var nativeWebView = new NativeWebView();
        var view = new WebBrowserTabView(
            uri =>
            {
                nativeWebView.Source = uri;
                return nativeWebView;
            },
            () => (true, null, false),
            getPageTitle: _ => Task.FromResult<string?>("\"Example page title\""));
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);
        var uri = new Uri("https://example.com/page");
        view.DataContext = viewModel;
        viewModel.BeginNavigation(uri);
        viewModel.CompleteNavigation(uri, isSuccess: true, canGoBack: false, canGoForward: false);

        await view.UpdatePageTitleAsync(uri);

        Assert.That(viewModel.Header.Value, Is.EqualTo("Example page title"));
        view.Dispose();
    }

    [AvaloniaTest]
    public void MissingLinuxRuntime_ShowsTheSetupGuideButtonWithoutCreatingAWebView()
    {
        Uri? launchedUri = null;
        var view = new WebBrowserTabView(
            _ => throw new AssertionException("A WebView must not be created when the runtime is unavailable."),
            () => (false, "Missing WebKit runtime.", true),
            uri =>
            {
                launchedUri = uri;
                return Task.FromResult(true);
            });
        var context = new TestEditorContext();
        using var viewModel = new WebBrowserTabViewModel(context);

        view.DataContext = viewModel;

        ContentControl host = view.FindControl<ContentControl>("WebViewHost")!;
        Button helpButton = view.FindControl<Button>("LinuxRuntimeHelpButton")!;
        Assert.Multiple(() =>
        {
            Assert.That(host.Content, Is.Null);
            Assert.That(helpButton.IsVisible, Is.True);
            Assert.That(viewModel.IsLinuxRuntimeHelpVisible.Value, Is.True);
            Assert.That(viewModel.ErrorMessage.Value, Does.Contain("Missing WebKit runtime."));
        });

        helpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.That(launchedUri, Is.EqualTo(WebBrowserTabView.LinuxWebViewSetupGuide));

        view.Dispose();
    }

    [AvaloniaTest]
    public void Dockable_DisposesReusableContentBeforeItsContext()
    {
        var disposeOrder = new List<string>();
        var context = new DisposableToolContext(disposeOrder);
        var content = new DisposableControl(disposeOrder);
        var dockable = new BeutlToolDockable(context, null!)
        {
            ToolContent = content
        };

        dockable.Dispose();

        Assert.That(disposeOrder, Is.EqualTo(new[] { "content", "context" }));
    }

    [AvaloniaTest]
    public void Dockable_DisposesACombinedContentContextOnlyOnce()
    {
        var combined = new CombinedToolContentContext();
        var dockable = new BeutlToolDockable(combined, null!)
        {
            ToolContent = combined
        };

        dockable.Dispose();
        dockable.Dispose();

        Assert.That(combined.DisposeCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void Dockable_DisposesContextWhenContentDisposalThrows()
    {
        var disposeOrder = new List<string>();
        var context = new DisposableToolContext(disposeOrder);
        var content = new ThrowingDisposableControl(disposeOrder);
        var dockable = new BeutlToolDockable(context, null!)
        {
            ToolContent = content
        };

        Assert.Throws<InvalidOperationException>(() => dockable.Dispose());
        Assert.That(disposeOrder, Is.EqualTo(new[] { "content", "context" }));
    }

    private sealed class DisposableControl(List<string> disposeOrder) : Control, IDisposable
    {
        public void Dispose()
        {
            disposeOrder.Add("content");
        }
    }

    private sealed class ThrowingDisposableControl(List<string> disposeOrder) : Control, IDisposable
    {
        public void Dispose()
        {
            disposeOrder.Add("content");
            throw new InvalidOperationException("Content disposal failed.");
        }
    }

    private sealed class DisposableToolContext(List<string> disposeOrder) : IToolContext
    {
        public ToolTabExtension Extension => DisposableToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Disposable");

        public void Dispose()
        {
            disposeOrder.Add("context");
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class DisposableToolExtension : ToolTabExtension
    {
        public static readonly DisposableToolExtension Instance = new();

        public override bool CanMultiple => false;

        public override bool ReuseContentAcrossActivation => true;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = null;
            return false;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = null;
            return false;
        }
    }

    private sealed class CombinedToolContentContext : Control, IToolContext
    {
        public int DisposeCount { get; private set; }

        public ToolTabExtension Extension => DisposableToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Combined");

        public void Dispose()
        {
            DisposeCount++;
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class TestEditorContext : IEditorContext
    {
        public CoreObject Object { get; } = new TestCoreObject();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestCoreObject : CoreObject;
}
