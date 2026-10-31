using System.Runtime.CompilerServices;
using Avalonia.Platform;
using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserWebViewEnvironmentTests
{
    private string _root = null!;

    private string FallbackFolder => Path.Combine(_root, "local", "WebView2");

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
            Assert.That(environment.ApplicationNameForUserAgent, Does.Match(@"^Version/\d+(\.\d+)+ Safari/605\.1\.15$"));
            Assert.That(environment.NonPersistentDataStore, Is.True);
        });
    }

    [TestCase("27.2", 27, 2, ExpectedResult = "Version/27.2 Safari/605.1.15")]
    [TestCase("18.1.1", 15, 1, ExpectedResult = "Version/18.1.1 Safari/605.1.15")]
    // A Safari update on an earlier macOS replaces the system WebKit too.
    [TestCase("26.1", 15, 7, ExpectedResult = "Version/26.1 Safari/605.1.15")]
    public string GetSafariApplicationName_StatesTheInstalledSafariVersion(string safariVersion, int osMajor, int osMinor)
        => BrowserWebViewEnvironment.GetSafariApplicationName(safariVersion, new Version(osMajor, osMinor));

    [TestCase(null, 26, 1, ExpectedResult = "Version/26.1 Safari/605.1.15")]
    [TestCase(null, 15, 6, ExpectedResult = "Version/18.6 Safari/605.1.15")]
    [TestCase("", 14, 4, ExpectedResult = "Version/17.4 Safari/605.1.15")]
    [TestCase("27.2 beta", 27, 2, ExpectedResult = "Version/27.2 Safari/605.1.15")]
    public string GetSafariApplicationName_WithoutAReadableVersion_FollowsMacOS(string? safariVersion, int osMajor, int osMinor)
        => BrowserWebViewEnvironment.GetSafariApplicationName(safariVersion, new Version(osMajor, osMinor));

    [Test]
    [Platform("MacOsX")]
    public void ReadBundleVersion_ReadsTheShortVersionString()
    {
        string bundle = Path.Combine(_root, "Safari.app");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleShortVersionString</key>
                <string>26.1</string>
            </dict>
            </plist>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(BrowserWebViewEnvironment.ReadBundleVersion(bundle), Is.EqualTo("26.1"));
            Assert.That(BrowserWebViewEnvironment.ReadBundleVersion(Path.Combine(_root, "Missing.app")), Is.Null);
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

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, FallbackFolder, processPath);

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

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, FallbackFolder, processPath);

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

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, FallbackFolder, processPath,
            (_, _) => throw new IOException("The profile is in use."));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(folder));
            Assert.That(Directory.Exists(folder), Is.True);
            Assert.That(File.Exists(Path.Combine(folder, "Local State")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(legacyFolder, "Local State")), Is.EqualTo("legacy"));
        });
    }

    [Test]
    public void PrepareWebView2UserDataFolder_UsesTheFallbackWhenTheFolderCannotBeCreated()
    {
        string processPath = Path.Combine(_root, "app", "Beutl.exe");
        string legacyFolder = processPath + ".WebView2";
        Directory.CreateDirectory(legacyFolder);
        // A file where the parent directory belongs blocks the folder on every platform.
        File.WriteAllText(Path.Combine(_root, "browser"), "");
        string folder = Path.Combine(_root, "browser", "WebView2");

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, FallbackFolder, processPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(FallbackFolder));
            Assert.That(Directory.Exists(legacyFolder), Is.True);
        });
    }

    [Test]
    public void PrepareWebView2UserDataFolder_WithoutAProcessPath_CreatesTheFolder()
    {
        string folder = Path.Combine(_root, "home", "browser", "WebView2");

        string result = BrowserWebViewEnvironment.PrepareWebView2UserDataFolder(folder, FallbackFolder, null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(folder));
            Assert.That(Directory.Exists(folder), Is.True);
        });
    }
}
