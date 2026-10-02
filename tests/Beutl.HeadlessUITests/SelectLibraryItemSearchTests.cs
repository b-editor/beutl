using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;

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
