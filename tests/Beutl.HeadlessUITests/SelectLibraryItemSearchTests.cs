using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
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
