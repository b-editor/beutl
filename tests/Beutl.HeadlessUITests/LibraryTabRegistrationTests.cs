using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Services;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryTabRegistrationTests
{
    // A format of its own, so that no other part of the app picks up the item while it is registered.
    private const string LateFormat = "Beutl.HeadlessUITests.LateItem";

    // One word that no built-in item contains, since a search matches any of its words.
    private const string LateName = "Xyzzy";

    private const string LateGroupName = "Xyzzy group";

    [AvaloniaTest]
    public void An_item_registered_after_the_tab_opens_is_listed_until_its_type_unloads()
    {
        using var viewModel = new LibraryTabViewModel(Mock.Of<IEditorContext>());
        LibraryItemViewModel[] before = [.. viewModel.LibraryItems];
        try
        {
            LibraryService.Current.Register<LateItem>(LateFormat, LateName);
            Dispatcher.UIThread.RunJobs();

            LibraryItemViewModel added = viewModel.LibraryItems.Last();
            Assert.Multiple(() =>
            {
                Assert.That(added.DisplayName, Is.EqualTo(LateName));
                Assert.That(viewModel.LibraryItems.Take(before.Length), Is.EqualTo(before),
                    "the items already listed keep their view models, so their nodes stay expanded");
                Assert.That(viewModel.AllItems.Select(item => item.Value), Has.Member(added));
            });
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateItem)]);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LibraryItems, Is.EqualTo(before));
            Assert.That(viewModel.AllItems.Select(item => item.Value.DisplayName), Has.No.Member(LateName));
        });
    }

    [AvaloniaTest]
    public void An_item_registered_into_a_group_goes_with_its_type()
    {
        using var viewModel = new LibraryTabViewModel(Mock.Of<IEditorContext>());
        LibraryItemViewModel[] before = [.. viewModel.LibraryItems];
        try
        {
            LibraryService.Current.RegisterGroup(LateGroupName, group => group.Add<LateItem>(LateFormat, LateName));
            Dispatcher.UIThread.RunJobs();

            LibraryItemViewModel group = viewModel.LibraryItems.Last();
            Assert.Multiple(() =>
            {
                Assert.That(group.DisplayName, Is.EqualTo(LateGroupName));
                Assert.That(group.Children.Select(child => child.DisplayName), Is.EqualTo(new[] { LateName }));
            });
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateItem)]);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.That(viewModel.LibraryItems, Is.EqualTo(before), "unloading the type empties the group, so the group goes too");
    }

    [AvaloniaTest]
    public async Task A_search_runs_again_when_an_item_is_registered()
    {
        using var viewModel = new LibraryTabViewModel(Mock.Of<IEditorContext>());
        await viewModel.Search(LateName, CancellationToken.None);
        Assert.That(viewModel.SearchResult, Is.Empty);
        try
        {
            LibraryService.Current.Register<LateItem>(LateFormat, LateName);

            await WaitUntil(() => viewModel.SearchResult.Any(item => item.Value.DisplayName == LateName));
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateItem)]);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class LateItem;
}
