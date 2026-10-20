using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Graphics.Shapes;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryTabSearchLifetimeTests
{
    [AvaloniaTest]
    public async Task Disposing_the_tab_cannot_publish_a_match_that_is_still_running()
    {
        using var viewModel = CreateViewModel();
        var type = new BlockingNameType();
        var item = new SingleTypeLibraryItem(KnownLibraryItemFormats.Drawable, type, "Needle");
        viewModel.AllItems.Add(LibraryItemViewModel.CreateFromLibraryItem(item));
        Task search = viewModel.Search("Needle", CancellationToken.None);
        try
        {
            await type.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Dispose();
            type.Release();
            await search.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.That(viewModel.SearchResult, Is.Empty,
                "A delayed match must not repopulate results after the library tab is closed.");
        }
        finally
        {
            type.Release();
            await search.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public async Task Disposing_the_tab_releases_a_queued_search_without_waiting_for_an_active_match()
    {
        using var viewModel = CreateViewModel();
        var type = new BlockingNameType();
        var item = new SingleTypeLibraryItem(KnownLibraryItemFormats.Drawable, type, "Needle");
        viewModel.AllItems.Add(LibraryItemViewModel.CreateFromLibraryItem(item));
        Task activeSearch = viewModel.Search("Needle", CancellationToken.None);
        Task? queuedSearch = null;
        try
        {
            await type.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queuedSearch = viewModel.Search("Needle", CancellationToken.None);
            viewModel.Dispose();

            await queuedSearch.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(activeSearch.IsCompleted, Is.False,
                "Queued work must stop through lifetime cancellation while the active matcher is still blocked.");
            type.Release();
            await activeSearch.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.That(viewModel.SearchResult, Is.Empty);
        }
        finally
        {
            type.Release();
            await activeSearch.WaitAsync(TimeSpan.FromSeconds(5));
            if (queuedSearch != null)
                await queuedSearch.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public void Search_orders_results_when_the_UI_receives_them_as_one_batch()
    {
        using var viewModel = CreateViewModel();
        var low = new LibraryItemViewModel { DisplayName = "Other", FullDisplayName = "Other", Description = "Rect" };
        var high = new LibraryItemViewModel { DisplayName = "Rect", FullDisplayName = "Rect" };
        var middle = LibraryItemViewModel.CreateFromLibraryItem(
            new SingleTypeLibraryItem(KnownLibraryItemFormats.Drawable, typeof(RectShape), "Other"));
        viewModel.AllItems.AddRange([low, high, middle]);

        // Keep the UI busy until the worker has queued all matches, as a render or layout pass can do.
        Task.Run(() => viewModel.Search("Rect", CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.SearchResult, Is.EqualTo(new[] { high, middle, low }));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Completed_search_checks_cancellation_before_deferred_UI_delivery(bool disposeTab)
    {
        using var viewModel = CreateViewModel();
        using var cancellation = new CancellationTokenSource();
        var item = new LibraryItemViewModel { DisplayName = "Needle", FullDisplayName = "Needle" };
        viewModel.AllItems.Add(item);
        Task.Run(() => viewModel.Search("Needle", cancellation.Token))
            .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

        if (disposeTab)
            viewModel.Dispose();
        else
            cancellation.Cancel();
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.SearchResult, Is.Empty,
            "Finishing the worker does not authorize a batch after cancellation or tab disposal.");
    }

    [AvaloniaTest]
    public void Only_the_latest_completed_search_is_published_when_UI_delivery_is_deferred()
    {
        using var viewModel = CreateViewModel();
        var first = new LibraryItemViewModel { DisplayName = "First", FullDisplayName = "First" };
        var second = new LibraryItemViewModel { DisplayName = "Second", FullDisplayName = "Second" };
        viewModel.AllItems.AddRange([first, second]);
        Task.Run(async () =>
        {
            await viewModel.Search("First", CancellationToken.None);
            await viewModel.Search("Second", CancellationToken.None);
        }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.That(viewModel.SearchResult, Is.EqualTo(new[] { second }));
    }

    // The drag starts from the pressed element's DataContext, so a row and its text must hold the same item.
    [AvaloniaTest]
    public async Task A_result_row_and_its_text_share_the_item_a_drag_reads()
    {
        using var viewModel = CreateViewModel();
        var item = new LibraryItemViewModel { DisplayName = "Needle", FullDisplayName = "Needle" };
        viewModel.AllItems.Add(item);
        var view = new SearchView { DataContext = viewModel };
        using var host = new ControlHost(view, 320, 240);
        ListBox list = view.FindControl<ListBox>("searchResult")!;

        foreach (string query in new[] { "", "Needle" })
        {
            view.FindControl<TextBox>("SearchBox")!.Text = query;
            await HeadlessTestHelpers.WaitUntilAsync(() => query.Length == 0 || viewModel.SearchResult.Count == 1);
            HeadlessTestHelpers.Render(2);
            Control row = list.ContainerFromIndex(0)!;
            Avalonia.Controls.TextBlock text = row.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                .First(t => t.Text == "Needle");
            Assert.That(row.DataContext, Is.SameAs(item), query);
            Assert.That(text.DataContext, Is.SameAs(item), query);
        }
    }

    private static LibraryTabViewModel CreateViewModel()
    {
        var viewModel = new LibraryTabViewModel();
        viewModel.AllItems.Clear();
        return viewModel;
    }

    private sealed class BlockingNameType() : TypeDelegator(typeof(RectShape))
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string Name
        {
            get
            {
                Entered.TrySetResult();
                _release.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                return "Needle";
            }
        }

        public void Release() => _release.TrySetResult();
    }
}
