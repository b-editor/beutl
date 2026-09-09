using System.Text.Json.Nodes;

using System.Runtime.CompilerServices;

using Avalonia.Platform;

using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Language;

using Moq;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class WebBrowserTabViewModelTests
{
    [Test]
    public void AddressSuggestions_KeepRecentSuccessfulVisitsWithoutDuplicates()
    {
        using var viewModel = new WebBrowserTabViewModel(new Mock<IEditorContext>().Object);
        for (int i = 0; i < 105; i++)
        {
            viewModel.CompleteNavigation(new Uri($"https://example.com/{i}"), true, false, false);
        }

        viewModel.CompleteNavigation(new Uri("https://example.com/100"), true, false, false);
        viewModel.CompleteNavigation(new Uri("https://failed.example/"), false, false, false);
        viewModel.CompleteNavigation(new Uri("https://user:password@example.com/"), true, false, false);
        viewModel.CompleteNavigation(WebBrowserTabViewModel.BlankPage, true, false, false);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AddressSuggestions, Has.Count.EqualTo(100));
            Assert.That(viewModel.AddressSuggestions[0], Is.EqualTo("https://example.com/100"));
            Assert.That(viewModel.AddressSuggestions, Is.Unique);
            Assert.That(viewModel.AddressSuggestions, Does.Not.Contain("https://example.com/0"));
        });
    }

    [Test]
    public void MacOSEnvironment_AddsSafariIdentificationBeforeNavigation()
    {
        // Avalonia creates these event args internally; this test only exercises their settings.
        var environment = (AppleWKWebViewEnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(
            typeof(AppleWKWebViewEnvironmentRequestedEventArgs));
        environment.NonPersistentDataStore = true;

        WebBrowserTabView.ConfigureMacOSWebViewEnvironment(null, environment);

        Assert.Multiple(() =>
        {
            Assert.That(environment.ApplicationNameForUserAgent, Is.EqualTo("Safari/605.1.15"));
            Assert.That(environment.NonPersistentDataStore, Is.True);
        });
    }

    [Test]
    public void InstalledWebKitGtk_IsAcceptedDespiteNativeDialogScenarioMetadata()
    {
        var info = new DetailedWebViewAdapterInfo(
            WebViewAdapterType.WebKitGtk,
            WebViewEngine.WebKit,
            "2.52.6",
            IsSupported: true,
            IsInstalled: true,
            UnavailableReason: null,
            WebViewEmbeddingScenario.NativeDialog);

        Assert.That(WebBrowserTabView.IsAdapterAvailable(info), Is.True);
    }

    [TestCase("Page title", "Page title")]
    [TestCase("  Page title  ", "Page title")]
    [TestCase("\"Page title\"", "Page title")]
    [TestCase("\"Escaped \\\"title\\\"\"", "Escaped \"title\"")]
    [TestCase("", null)]
    [TestCase("   ", null)]
    public void NormalizePageTitle_HandlesDirectAndJsonEncodedResults(string result, string? expected)
    {
        Assert.That(WebBrowserTabView.NormalizePageTitle(result), Is.EqualTo(expected));
    }


    [Test]
    public void BlankPage_UsesTheNewTabHeader()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Header.Value, Is.EqualTo(Strings.NewTab));
            Assert.That(viewModel.HasWebAddress.Value, Is.False);
        });
    }

    [TestCase("example.com", "https://example.com/")]
    [TestCase(" localhost:5000/path ", "https://localhost:5000/path")]
    [TestCase("http://example.com/page", "http://example.com/page")]
    [TestCase("https://example.com/page?q=1", "https://example.com/page?q=1")]
    public void TryNormalizeAddress_AcceptsWebAddresses(string address, string expected)
    {
        bool result = WebBrowserTabViewModel.TryNormalizeAddress(address, out Uri uri);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(uri.AbsoluteUri, Is.EqualTo(expected));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void TryNormalizeAddress_UsesBlankPageForEmptyInput(string address)
    {
        bool result = WebBrowserTabViewModel.TryNormalizeAddress(address, out Uri uri);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(uri, Is.EqualTo(WebBrowserTabViewModel.BlankPage));
        });
    }

    [TestCase("file:///tmp/page.html")]
    [TestCase("ftp://example.com/file")]
    [TestCase("mailto:user@example.com")]
    [TestCase("javascript:alert('test')")]
    [TestCase("not a host")]
    public void TryNormalizeAddress_RejectsUnsupportedOrInvalidAddresses(string address)
    {
        Assert.That(WebBrowserTabViewModel.TryNormalizeAddress(address, out _), Is.False);
    }

    [Test]
    public void NavigationState_UpdatesAddressHeaderAndHistoryAvailability()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);
        var uri = new Uri("https://docs.beutl.beditor.net/get-started/");

        viewModel.BeginNavigation(uri);
        viewModel.CompleteNavigation(uri, isSuccess: true, canGoBack: true, canGoForward: false);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CurrentUri, Is.EqualTo(uri));
            Assert.That(viewModel.Address.Value, Is.EqualTo(uri.AbsoluteUri));
            Assert.That(viewModel.Header.Value, Does.EndWith(": docs.beutl.beditor.net"));
            Assert.That(viewModel.IsLoading.Value, Is.False);
            Assert.That(viewModel.CanGoBack.Value, Is.True);
            Assert.That(viewModel.CanGoForward.Value, Is.False);
            Assert.That(viewModel.ErrorMessage.Value, Is.Null);
            Assert.That(viewModel.HasWebAddress.Value, Is.True);
        });
    }

    [Test]
    public void PageTitle_ReplacesTheHostBasedHeader()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);
        var uri = new Uri("https://example.com/page");

        viewModel.BeginNavigation(uri);
        viewModel.CompleteNavigation(uri, isSuccess: true, canGoBack: false, canGoForward: false);
        viewModel.SetPageTitle(uri, "Example page");

        Assert.That(viewModel.Header.Value, Is.EqualTo("Example page"));
    }

    [Test]
    public void StalePageTitle_DoesNotReplaceTheCurrentPageHeader()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);
        var previousUri = new Uri("https://previous.example/page");
        var currentUri = new Uri("https://current.example/page");

        viewModel.BeginNavigation(currentUri);
        viewModel.SetPageTitle(previousUri, "Previous page");

        Assert.That(viewModel.Header.Value, Does.EndWith(": current.example"));
    }

    [Test]
    public void NavigationFailure_IsExposedToTheUser()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);
        var uri = new Uri("https://example.invalid/");

        viewModel.CompleteNavigation(uri, isSuccess: false, canGoBack: false, canGoForward: false);

        Assert.That(viewModel.ErrorMessage.Value, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void StoppedNavigation_DoesNotReportALoadFailure()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);
        var uri = new Uri("https://example.com/slow");

        viewModel.BeginNavigation(uri);
        viewModel.StopNavigation();
        viewModel.CompleteNavigation(uri, isSuccess: false, canGoBack: false, canGoForward: false);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsLoading.Value, Is.False);
            Assert.That(viewModel.ErrorMessage.Value, Is.Null);
        });
    }

    [Test]
    public void ReportActionFailure_UsesTheLocalizedBrowserMessage()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);

        viewModel.ReportActionFailure();

        Assert.That(viewModel.ErrorMessage.Value, Is.EqualTo(Strings.WebBrowserActionFailed));
    }

    [Test]
    public void SetWebViewUnavailable_ControlsLinuxSetupHelpVisibility()
    {
        var editorContext = new Mock<IEditorContext>();
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);

        viewModel.SetWebViewUnavailable("Missing runtime.", showLinuxRuntimeHelp: true);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsLinuxRuntimeHelpVisible.Value, Is.True);
            Assert.That(viewModel.ErrorMessage.Value, Does.Contain("Missing runtime."));
        });
    }

    [Test]
    public void CurrentAddress_RoundTripsThroughToolState()
    {
        var editorContext = new Mock<IEditorContext>();
        var uri = new Uri("https://example.com/path?q=1");
        var json = new JsonObject();

        using (var source = new WebBrowserTabViewModel(editorContext.Object))
        {
            source.BeginNavigation(uri);
            source.WriteToJson(json);
        }

        using var restored = new WebBrowserTabViewModel(editorContext.Object);
        restored.ReadFromJson(json);

        Assert.Multiple(() =>
        {
            Assert.That(restored.CurrentUri, Is.EqualTo(uri));
            Assert.That(restored.Address.Value, Is.EqualTo(uri.AbsoluteUri));
        });
    }

    [Test]
    public void TryOpenNewTab_UsesTheRequestedAddress()
    {
        var editorContext = new Mock<IEditorContext>();
        IToolContext? openedContext = null;
        editorContext
            .Setup(x => x.OpenToolTab(It.IsAny<IToolContext>()))
            .Callback<IToolContext>(context => openedContext = context)
            .Returns(true);
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);
        var uri = new Uri("https://example.com/new-window");

        bool result = viewModel.TryOpenNewTab(uri);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(openedContext, Is.TypeOf<WebBrowserTabViewModel>());
                Assert.That(((WebBrowserTabViewModel)openedContext!).CurrentUri, Is.EqualTo(uri));
            });
        }
        finally
        {
            openedContext?.Dispose();
        }
    }

    [Test]
    public void TryOpenNewTab_AllowsABlankPage()
    {
        var editorContext = new Mock<IEditorContext>();
        IToolContext? openedContext = null;
        editorContext
            .Setup(x => x.OpenToolTab(It.IsAny<IToolContext>()))
            .Callback<IToolContext>(context => openedContext = context)
            .Returns(true);
        using var viewModel = new WebBrowserTabViewModel(editorContext.Object);

        bool result = viewModel.TryOpenNewTab(WebBrowserTabViewModel.BlankPage);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(((WebBrowserTabViewModel)openedContext!).CurrentUri,
                    Is.EqualTo(WebBrowserTabViewModel.BlankPage));
                Assert.That(openedContext!.Header.Value, Is.EqualTo(Strings.NewTab));
            });
        }
        finally
        {
            openedContext?.Dispose();
        }
    }
}
