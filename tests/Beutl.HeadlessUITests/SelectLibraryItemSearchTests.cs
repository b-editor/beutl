using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class SelectLibraryItemSearchTests
{
    [AvaloniaTest]
    public async Task A_preferred_catalog_completion_cannot_append_to_newer_ShowAll_results()
    {
        var preferred = CreateCompletion();
        var all = CreateCompletion();
        var viewModel = CreateViewModel(preferred.Task, all.Task);
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        Task preferredSearch = viewModel.SearchTask;
        viewModel.ShowAll.Value = true;
        Dispatcher.UIThread.RunJobs();
        Task allSearch = viewModel.SearchTask;
        Assert.That(allSearch, Is.Not.SameAs(preferredSearch));

        all.SetResult([CreateItem(typeof(EllipseShape), "All catalog")]);
        await allSearch.WaitAsync(TimeSpan.FromSeconds(5));
        preferred.SetResult([CreateItem(typeof(RectShape), "Preferred catalog")]);
        await preferredSearch.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "All catalog" }));
    }

    [AvaloniaTest]
    public async Task A_ShowAll_completion_cannot_append_after_switching_back_to_preferred_results()
    {
        var preferred = CreateCompletion();
        var all = CreateCompletion();
        var viewModel = CreateViewModel(preferred.Task, all.Task);
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        Task initialSearch = viewModel.SearchTask;
        viewModel.ShowAll.Value = true;
        Dispatcher.UIThread.RunJobs();
        Task allSearch = viewModel.SearchTask;
        viewModel.ShowAll.Value = false;
        Dispatcher.UIThread.RunJobs();
        Task latestSearch = viewModel.SearchTask;
        Assert.That(latestSearch, Is.Not.SameAs(initialSearch));

        preferred.SetResult([CreateItem(typeof(RectShape), "Preferred catalog")]);
        await latestSearch.WaitAsync(TimeSpan.FromSeconds(5));
        await initialSearch.WaitAsync(TimeSpan.FromSeconds(5));
        all.SetResult([CreateItem(typeof(EllipseShape), "All catalog")]);
        await allSearch.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Preferred catalog" }));
    }

    [AvaloniaTest]
    public async Task Repeated_switches_waiting_for_the_same_catalog_publish_each_item_once()
    {
        var preferred = CreateCompletion();
        var all = CreateCompletion();
        var viewModel = CreateViewModel(preferred.Task, all.Task);
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        var searches = new List<Task> { viewModel.SearchTask };
        viewModel.ShowAll.Value = true;
        Dispatcher.UIThread.RunJobs();
        searches.Add(viewModel.SearchTask);
        viewModel.ShowAll.Value = false;
        Dispatcher.UIThread.RunJobs();
        searches.Add(viewModel.SearchTask);
        viewModel.ShowAll.Value = true;
        Dispatcher.UIThread.RunJobs();
        searches.Add(viewModel.SearchTask);
        Assert.That(searches.Distinct().Count(), Is.EqualTo(4));

        preferred.SetResult([CreateItem(typeof(RectShape), "Preferred catalog")]);
        all.SetResult([CreateItem(typeof(EllipseShape), "All catalog")]);
        await Task.WhenAll(searches).WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "All catalog" }));
    }

    [AvaloniaTest]
    public async Task Cached_catalog_refreshes_keep_one_current_set_of_items()
    {
        var viewModel = CreateViewModel(
            Task.FromResult(new[] { CreateItem(typeof(RectShape), "Preferred catalog") }),
            Task.FromResult(new[] { CreateItem(typeof(EllipseShape), "All catalog") }));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        await viewModel.SearchTask;
        Dispatcher.UIThread.RunJobs();
        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Preferred catalog" }));

        viewModel.ShowAll.Value = true;
        Dispatcher.UIThread.RunJobs();
        await viewModel.SearchTask;
        Dispatcher.UIThread.RunJobs();
        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "All catalog" }));

        viewModel.ShowAll.Value = false;
        Dispatcher.UIThread.RunJobs();
        await viewModel.SearchTask;
        Dispatcher.UIThread.RunJobs();
        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Preferred catalog" }));
    }

    // The editor creates the chosen type with its parameterless constructor.
    [AvaloniaTest]
    public async Task Show_all_lists_only_types_the_editor_can_create()
    {
        var viewModel = new SelectLibraryItemDialogViewModel("Beutl.HeadlessUITests.PickerProbe", typeof(ILibraryPickerProbe));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;

        PinnableLibraryItem[] items = await viewModel.LoadAllItems().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.That(
            items.Select(i => i.UserData).OfType<SingleTypeLibraryItem>().Select(i => i.ImplementationType),
            Is.EquivalentTo(new[] { typeof(ConstructibleLibraryPickerProbe), typeof(ValueLibraryPickerProbe) }));
    }

    // A native popup placed at the pointer keeps the size it opened with, so the search box must not grow the picker.
    [AvaloniaTest]
    public async Task Showing_the_search_box_keeps_the_picker_height()
    {
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 320, Height = 480 };
        var viewModel = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        var flyout = new LibraryItemPickerFlyout(viewModel);
        try
        {
            window.Show();
            flyout.ShowAt(host, true);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(2);
            var presenter = (LibraryItemPickerFlyoutPresenter)flyout.Popup.Child!;
            double height = presenter.Bounds.Height;

            presenter.ShowSearchBox = true;
            HeadlessTestHelpers.Render(2);

            Assert.That(presenter.Bounds.Height, Is.EqualTo(height));
        }
        finally
        {
            flyout.Hide();
            window.Close();
        }
    }

    // Ctrl+F, a search word, Down and Enter pick the search result Down highlighted. The app styles every TextBox to
    // move the focus away on Enter (TestApp.axaml does too), which must not swallow the Enter that confirms the picker.
    [AvaloniaTest]
    public async Task Enter_in_the_search_box_picks_the_result_selected_with_Down()
    {
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 320, Height = 480 };
        var viewModel = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        try
        {
            window.Show();
            Task<object?> picked = LibraryItemPickerHelper.ShowAsync(host, viewModel, KnownLibraryItemFormats.FilterEffect);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(3);
            var presenter = window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
            string word = viewModel.Items[^1].DisplayName;
            int unfiltered = viewModel.Items.Count;

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            HeadlessTestHelpers.Render();
            window.KeyTextInput(word);
            // The ranking decides which result comes first, so wait only until the list is filtered by the word.
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SearchText.Value == word
                && viewModel.Items.Count < unfiltered && viewModel.Items.Any(i => i.DisplayName == word));
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(2);
            PinnableLibraryItem first = viewModel.Items[0];

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            HeadlessTestHelpers.Render();
            Assert.That(presenter.SelectedItem, Is.SameAs(first));

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            HeadlessTestHelpers.Render();

            object? result = await picked.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(result, Is.EqualTo(((SingleTypeLibraryItem)first.UserData).ImplementationType));
        }
        finally
        {
            window.Close();
        }
    }

    private static void Press(Window window, Key key, PhysicalKey physical, string? text)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, text);
        window.KeyRelease(key, RawInputModifiers.None, physical, text);
    }

    // The search applies after a 100 ms pause, so nothing here may wait between typing and the key that acts on it.
    [AvaloniaTest]
    public async Task Down_and_Enter_right_after_typing_pick_from_the_results_for_the_typed_text()
    {
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 320, Height = 480 };
        var viewModel = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        try
        {
            window.Show();
            Task<object?> picked = LibraryItemPickerHelper.ShowAsync(host, viewModel, KnownLibraryItemFormats.FilterEffect);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(3);
            PinnableLibraryItem firstOfAll = viewModel.Items[0];
            string word = viewModel.Items[^1].DisplayName;
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            HeadlessTestHelpers.Render();

            window.KeyTextInput(word);
            Press(window, Key.Down, PhysicalKey.ArrowDown, null);
            Press(window, Key.Enter, PhysicalKey.Enter, "\r");

            object? result = await picked.WaitAsync(TimeSpan.FromSeconds(5));
            await viewModel.SearchTask;
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items[0], Is.Not.SameAs(firstOfAll));
                Assert.That(result, Is.EqualTo(((SingleTypeLibraryItem)viewModel.Items[0].UserData).ImplementationType));
                Assert.That(viewModel.Items.Any(item => item.DisplayName == word), Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Enter_right_after_typing_does_not_pick_what_the_old_list_had_selected()
    {
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 320, Height = 480 };
        var viewModel = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        try
        {
            window.Show();
            Task<object?> picked = LibraryItemPickerHelper.ShowAsync(host, viewModel, KnownLibraryItemFormats.FilterEffect);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(3);
            string word = viewModel.Items[^1].DisplayName;
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            HeadlessTestHelpers.Render();
            Press(window, Key.Down, PhysicalKey.ArrowDown, null);
            Assert.That(viewModel.SelectedItem.Value, Is.Not.Null);

            window.KeyTextInput(word);
            Press(window, Key.Enter, PhysicalKey.Enter, "\r");

            Assert.That(await picked.WaitAsync(TimeSpan.FromSeconds(5)), Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    // The pause ending after a key already applied the text must not rebuild the list and drop the selection.
    [AvaloniaTest]
    public async Task The_pause_ending_after_Down_keeps_the_selection()
    {
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 320, Height = 480 };
        var viewModel = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        try
        {
            window.Show();
            Task<object?> picked = LibraryItemPickerHelper.ShowAsync(host, viewModel, KnownLibraryItemFormats.FilterEffect);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(3);
            string word = viewModel.Items[^1].DisplayName;
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            HeadlessTestHelpers.Render();

            window.KeyTextInput(word);
            Press(window, Key.Down, PhysicalKey.ArrowDown, null);
            PinnableLibraryItem? selected = viewModel.SelectedItem.Value;
            Assert.That(selected, Is.Not.Null);
            await Task.Delay(300);
            HeadlessTestHelpers.Settle();

            Assert.That(viewModel.SelectedItem.Value, Is.SameAs(selected));
            Press(window, Key.Enter, PhysicalKey.Enter, "\r");
            Assert.That(await picked.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.EqualTo(((SingleTypeLibraryItem)selected!.UserData).ImplementationType));
        }
        finally
        {
            window.Close();
        }
    }

    // ShowAll searches right away, so the text it searched for is the one a later key compares with.
    [AvaloniaTest]
    public async Task Going_back_to_the_earlier_text_after_ShowAll_searched_for_another_finds_it_again()
    {
        PinnableLibraryItem[] items = [CreateItem(typeof(RectShape), "Rectangle"), CreateItem(typeof(EllipseShape), "Ellipse")];
        var viewModel = CreateViewModel(Task.FromResult(items), Task.FromResult(items));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        await viewModel.SearchTask;
        viewModel.SearchText.Value = "Rectangle";
        viewModel.FlushSearch();
        await viewModel.SearchTask;

        viewModel.SearchText.Value = "Ellipse";
        viewModel.ShowAll.Value = true;
        await viewModel.SearchTask;
        Dispatcher.UIThread.RunJobs();
        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Ellipse" }));

        viewModel.SearchText.Value = "Rectangle";
        viewModel.FlushSearch();
        await viewModel.SearchTask;
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Rectangle" }));
    }

    // The search reads the text once the catalog has loaded, which can be newer than the text that started it.
    [AvaloniaTest]
    public async Task Going_back_to_the_text_a_search_started_with_after_it_found_a_newer_one_finds_it_again()
    {
        var preferred = CreateCompletion();
        var viewModel = CreateViewModel(preferred.Task, Task.FromResult<PinnableLibraryItem[]>([]));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        viewModel.SearchText.Value = "Rectangle";
        viewModel.FlushSearch();
        Task search = viewModel.SearchTask;
        viewModel.SearchText.Value = "Ellipse";

        preferred.SetResult([CreateItem(typeof(RectShape), "Rectangle"), CreateItem(typeof(EllipseShape), "Ellipse")]);
        await search.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Ellipse" }));

        viewModel.SearchText.Value = "Rectangle";
        viewModel.FlushSearch();
        await viewModel.SearchTask;
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(new[] { "Rectangle" }));
    }

    private static SelectLibraryItemDialogViewModel CreateViewModel(
        Task<PinnableLibraryItem[]> preferred, Task<PinnableLibraryItem[]> all)
    {
        Assert.That(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.Drawable), Is.Not.Empty);
        return new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.Drawable, typeof(Drawable), preferred, all);
    }

    private static TaskCompletionSource<PinnableLibraryItem[]> CreateCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static PinnableLibraryItem CreateItem(Type type, string displayName)
    {
        var item = new SingleTypeLibraryItem(KnownLibraryItemFormats.Drawable, type, displayName);
        return new PinnableLibraryItem(displayName, false, item);
    }
}

public interface ILibraryPickerProbe
{
}

public sealed class ConstructibleLibraryPickerProbe : ILibraryPickerProbe
{
}

public sealed class NonConstructibleLibraryPickerProbe(int value) : ILibraryPickerProbe
{
    public int Value { get; } = value;
}

public struct ValueLibraryPickerProbe : ILibraryPickerProbe
{
}
