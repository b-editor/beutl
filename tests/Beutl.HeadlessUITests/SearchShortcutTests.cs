using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Pages;
using Beutl.Pages.ExtensionsPages;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SearchShortcutTests
{
    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control)]
    [TestCase(RawInputModifiers.Meta)]
    public async Task Library_opens_search_and_selects_the_existing_query(RawInputModifiers modifier)
    {
        using var library = new LibraryTabViewModel(TestShell.Library, TestShell.Configuration.EditorConfig);
        var view = new LibraryTabView { DataContext = library };
        using var host = new ControlHost(view, 640, 360);
        var tabs = view.FindControl<TabStrip>("tabStrip")!;
        var initialTab = (TabStripItem)tabs.SelectedItem!;
        Assert.That(initialTab.Focus(), Is.True);

        PressFind(host.Window, modifier | RawInputModifiers.Shift);
        Assert.That(tabs.SelectedItem, Is.SameAs(initialTab));
        PressFind(host.Window, modifier);

        var page = view.GetVisualDescendants().OfType<SearchView>().Single();
        var search = page.FindControl<TextBox>("SearchBox")!;
        Assert.That(tabs.SelectedIndex, Is.Zero);
        Assert.That(search.IsFocused, Is.True);
        host.Window.KeyTextInput("Blur");

        await HeadlessTestHelpers.WaitUntilAsync(() => library.SearchResult.Count > 0);
        HeadlessTestHelpers.Render();
        var results = page.FindControl<ListBox>("searchResult")!;
        Assert.That(results.ContainerFromIndex(0)!.Focus(), Is.True);
        PressFind(host.Window, modifier);
        Assert.That(search.IsFocused, Is.True);
        Assert.That(search.SelectedText, Is.EqualTo("Blur"));
        HeadlessCapture.SaveIfRequested(view, nameof(SearchShortcutTests), "library");
        host.Window.KeyTextInput("Glow");
        Assert.That(search.Text, Is.EqualTo("Glow"));
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control)]
    [TestCase(RawInputModifiers.Meta)]
    public void Store_selects_the_query_from_the_current_page(RawInputModifiers modifier)
    {
        var store = new ExtensionsPage();
        using var host = new ControlHost(store);
        var frame = store.FindControl<FAFrame>("frame")!;
        frame.Navigate(typeof(LibraryPage));
        HeadlessTestHelpers.Render();
        var page = (LibraryPage)frame.Content!;
        var tab = page.FindControl<TabStripItem>("packagesTab")!;
        var search = store.FindControl<TextBox>("searchTextBox")!;
        search.Text = "Beutl";
        Assert.That(tab.Focus(), Is.True);

        PressFind(store, modifier | RawInputModifiers.Shift);
        Assert.That(tab.IsFocused, Is.True);
        PressFind(store, modifier);

        Assert.That(search.IsFocused, Is.True);
        Assert.That(search.SelectedText, Is.EqualTo("Beutl"));
        HeadlessCapture.SaveIfRequested(store, nameof(SearchShortcutTests), "store");
        store.KeyTextInput("FFmpeg");
        Assert.That(search.Text, Is.EqualTo("FFmpeg"));
        PressFind(store, modifier);
        Assert.That(search.SelectedText, Is.EqualTo("FFmpeg"));
        Assert.That(frame.Content, Is.SameAs(page));
    }

    private static void PressFind(Window window, RawInputModifiers modifiers)
    {
        window.KeyPress(Key.F, modifiers, PhysicalKey.F, "f");
        window.KeyRelease(Key.F, modifiers, PhysicalKey.F, "f");
    }
}
