using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;

using Beutl.Editor.Components.WebBrowserTab.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class WebBrowserAddressBoxTests
{
    [AvaloniaTest]
    public async Task SearchSuggestions_IgnoreOldResponsesAndClearOnBlur()
    {
        var first = new TaskCompletionSource<IReadOnlyList<string>>();
        var second = new TaskCompletionSource<IReadOnlyList<string>>();
        var address = new WebBrowserAddressBox
        {
            SuggestionDelay = TimeSpan.Zero,
            SuggestionProvider = (query, _) => query == "old" ? first.Task : second.Task
        };
        IReadOnlyList<string> shown = [];
        address.SearchSuggestionsChanged += suggestions => shown = suggestions;
        var other = new Button();
        var window = new Window { Content = new StackPanel { Children = { address, other } } };
        try
        {
            window.Show();
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            Task oldRequest = address.RefreshSearchSuggestionsAsync("old");
            Task newRequest = address.RefreshSearchSuggestionsAsync("new");
            second.SetResult(["new result"]);
            await newRequest;
            first.SetResult(["old result"]);
            await oldRequest;
            Assert.That(shown, Is.EqualTo(new[] { "new result" }));
            other.Focus();
            Assert.That(shown, Is.Empty);

            address.Focus();
            address.SuggestionProvider = (_, _) => throw new System.Net.Http.HttpRequestException("offline");
            await address.RefreshSearchSuggestionsAsync("offline");
            Assert.That(shown, Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Focus_SelectsFullAddress_AndBlurOnlyChangesPresentation()
    {
        const string url = "https://example.com:8443/path?q=value#section";
        var source = new TextBox { Text = url };
        var address = new WebBrowserAddressBox();
        address.Bind(WebBrowserAddressBox.AddressProperty,
            new Binding(nameof(TextBox.Text)) { Source = source, Mode = BindingMode.TwoWay });
        var other = new Button { Content = "Other" };
        var window = new Window { Content = new StackPanel { Children = { other, address } } };
        try
        {
            window.Show();
            other.Focus();
            Assert.That(address.Text, Is.EqualTo("example.com:8443"));

            address.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(address.Text, Is.EqualTo(url));
                Assert.That(address.SelectedText, Is.EqualTo(url));
            });

            other.Focus();
            Assert.Multiple(() =>
            {
                Assert.That(address.Text, Is.EqualTo("example.com:8443"));
                Assert.That(address.Address, Is.EqualTo(url));
                Assert.That(source.Text, Is.EqualTo(url));
            });

            Point point = address.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.That(address.SelectedText, Is.EqualTo(url));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Typing_CompletesHistory_AndFurtherTypingReplacesSelectedSuffix()
    {
        var address = new WebBrowserAddressBox
        {
            Suggestions = new[] { "http://example.com/path" }
        };
        var window = new Window { Content = address };
        try
        {
            window.Show();
            address.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput("exa");
            Assert.Multiple(() =>
            {
                Assert.That(address.Address, Is.EqualTo("http://example.com/path"));
                Assert.That(address.SelectedText, Is.EqualTo("mple.com/path"));
            });

            window.KeyTextInput("z");
            Assert.That(address.Address, Is.EqualTo("http://exaz"));
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Assert.That(address.Text, Is.EqualTo("http://exa"));
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase("exa", "http://example.com/path")]
    [TestCase("http://exa", "http://example.com/path")]
    [TestCase("https://exa", null)]
    [TestCase("example.com/Path", null)]
    [TestCase("unrelated", null)]
    [TestCase("", null)]
    public void Completion_PreservesSchemeAndPathCase(string prefix, string? expected)
    {
        Assert.That(WebBrowserAddressBox.FindCompletion(prefix, new[] { "http://example.com/path" }),
            Is.EqualTo(expected));
    }
}
