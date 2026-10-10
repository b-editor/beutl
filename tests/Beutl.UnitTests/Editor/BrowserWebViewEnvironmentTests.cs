using System.Runtime.CompilerServices;
using Avalonia.Platform;
using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserWebViewEnvironmentTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "beutl-webview-environment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // Avalonia creates these event args internally; the tests only exercise their settings.
    private static T CreateEventArgs<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Test]
    public void MacOSEnvironment_AddsSafariIdentificationBeforeNavigation()
    {
        var environment = CreateEventArgs<AppleWKWebViewEnvironmentRequestedEventArgs>();
        environment.NonPersistentDataStore = true;

        BrowserWebViewEnvironment.Configure(null, environment);

        Assert.Multiple(() =>
        {
            Assert.That(environment.ApplicationNameForUserAgent, Is.EqualTo("Safari/605.1.15"));
            Assert.That(environment.NonPersistentDataStore, Is.True);
        });
    }

    [Test]
    public void WindowsEnvironment_KeepsEveryWebViewInOneProfileUnderTheBeutlHome()
    {
        var tab = CreateEventArgs<WindowsWebView2EnvironmentRequestedEventArgs>();
        var settingsPage = CreateEventArgs<WindowsWebView2EnvironmentRequestedEventArgs>();

        BrowserWebViewEnvironment.Configure(null, tab);
        BrowserWebViewEnvironment.Configure(null, settingsPage);

        Assert.Multiple(() =>
        {
            Assert.That(tab.UserDataFolder,
                Is.EqualTo(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "WebView2")));
            Assert.That(settingsPage.UserDataFolder, Is.EqualTo(tab.UserDataFolder));
        });
    }

    [Test]
    public void PrepareWebView2UserDataFolder_MovesTheProfileBesideTheExecutable()
    {
        string processPath = Path.Combine(_root, "app", "Beutl.exe");
        string legacyFolder = processPath + ".WebView2";
        Directory.CreateDirectory(Path.Combine(legacyFolder, "EBWebView"));
        File.WriteAllText(Path.Combine(legacyFolder, "EBWebView", "Local State"), "state");
        string folder = Path.Combine(_root, "home", "browser", "WebView2");

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, processPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(folder));
            Assert.That(File.ReadAllText(Path.Combine(folder, "EBWebView", "Local State")), Is.EqualTo("state"));
            Assert.That(Directory.Exists(legacyFolder), Is.False);
        });
    }

    [Test]
    public void PrepareWebView2UserDataFolder_KeepsAnExistingProfile()
    {
        string processPath = Path.Combine(_root, "app", "Beutl.exe");
        string legacyFolder = processPath + ".WebView2";
        Directory.CreateDirectory(legacyFolder);
        File.WriteAllText(Path.Combine(legacyFolder, "Local State"), "legacy");
        string folder = Path.Combine(_root, "home", "browser", "WebView2");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Local State"), "current");

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, processPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(folder));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Local State")), Is.EqualTo("current"));
            Assert.That(File.ReadAllText(Path.Combine(legacyFolder, "Local State")), Is.EqualTo("legacy"));
        });
    }

    [Test]
    public void PrepareWebView2UserDataFolder_StartsANewProfileWhenTheMoveFails()
    {
        string processPath = Path.Combine(_root, "app", "Beutl.exe");
        string legacyFolder = processPath + ".WebView2";
        Directory.CreateDirectory(legacyFolder);
        File.WriteAllText(Path.Combine(legacyFolder, "Local State"), "legacy");
        string folder = Path.Combine(_root, "home", "browser", "WebView2");

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, processPath,
            (_, _) => throw new IOException("The profile is in use."));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(folder));
            Assert.That(File.ReadAllText(Path.Combine(legacyFolder, "Local State")), Is.EqualTo("legacy"));
            Assert.That(Directory.Exists(folder), Is.False);
        });
        // WebView2 creates the new profile itself; the location must still accept it.
        Directory.CreateDirectory(result);
        File.WriteAllText(Path.Combine(result, "Local State"), "new");
        Assert.That(File.ReadAllText(Path.Combine(result, "Local State")), Is.EqualTo("new"));
    }

    [Test]
    public void PrepareWebView2UserDataFolder_DoesNotThrowWhenTheDestinationCannotBeCreated()
    {
        string processPath = Path.Combine(_root, "app", "Beutl.exe");
        string legacyFolder = processPath + ".WebView2";
        Directory.CreateDirectory(legacyFolder);
        // A file where the parent directory belongs makes the migration fail on every platform.
        File.WriteAllText(Path.Combine(_root, "browser"), "");
        string folder = Path.Combine(_root, "browser", "WebView2");

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, processPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(folder));
            Assert.That(Directory.Exists(legacyFolder), Is.True);
        });
    }

    [Test]
    public void PrepareWebView2UserDataFolder_WithoutAProcessPath_ReturnsTheFolder()
    {
        string folder = Path.Combine(_root, "home", "browser", "WebView2");

        Assert.That(BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, null), Is.EqualTo(folder));
    }
}
