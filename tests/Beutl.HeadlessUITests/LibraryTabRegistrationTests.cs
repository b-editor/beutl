using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.NodeGraph;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryTabRegistrationTests
{
    private LibraryService _library = null!;

    [SetUp]
    public void SetUpLibrary() => _library = new LibraryService();

    [TearDown]
    public void DisposeLibrary() => _library.Dispose();

    private const string LateFormat = "Beutl.HeadlessUITests.LateItem";

    // One word that no built-in item contains, since a search matches any of its words.
    private const string LateName = "Xyzzy";

    private const string LateGroupName = "Xyzzy group";

    [AvaloniaTest]
    public void An_item_registered_after_the_tab_opens_is_listed_until_its_type_unloads()
    {
        using var viewModel = new LibraryTabViewModel(_library);
        LibraryItemViewModel[] before = [.. viewModel.LibraryItems];
        try
        {
            _library.Register<LateItem>(LateFormat, LateName);
            Dispatcher.UIThread.RunJobs();

            LibraryItemViewModel added = viewModel.LibraryItems.Last();
            Assert.Multiple(() =>
            {
                Assert.That(added.DisplayName, Is.EqualTo(LateName));
                Assert.That(viewModel.LibraryItems.Take(before.Length), Is.EqualTo(before),
                    "the items already listed keep their view models, so their nodes stay expanded");
                Assert.That(viewModel.AllItems, Has.Member(added));
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
            Assert.That(viewModel.AllItems.Select(item => item.DisplayName), Has.No.Member(LateName));
        });
    }

    [AvaloniaTest]
    public void A_node_registered_after_the_tab_opens_is_listed_until_its_type_unloads()
    {
        using var viewModel = new LibraryTabViewModel(_library);
        LibraryItemViewModel[] before = [.. viewModel.Nodes];
        try
        {
            GraphNodeRegistry.RegisterNode<LateNode>(LateName);
            Dispatcher.UIThread.RunJobs();

            LibraryItemViewModel added = viewModel.Nodes.Last();
            Assert.Multiple(() =>
            {
                Assert.That(added.DisplayName, Is.EqualTo(LateName));
                Assert.That(viewModel.Nodes.Take(before.Length), Is.EqualTo(before),
                    "the nodes already listed keep their view models, so their nodes in the tree stay expanded");
                Assert.That(viewModel.AllItems, Has.Member(added));
            });
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateNode)]);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Nodes, Is.EqualTo(before));
            Assert.That(viewModel.AllItems.Select(item => item.DisplayName), Has.No.Member(LateName));
        });
    }

    [AvaloniaTest]
    public void A_node_registered_into_a_group_goes_with_its_type()
    {
        using var viewModel = new LibraryTabViewModel(_library);
        LibraryItemViewModel[] before = [.. viewModel.Nodes];
        try
        {
            GraphNodeRegistry.RegisterNodes(LateGroupName).Add<LateNode>(LateName).Register();
            Dispatcher.UIThread.RunJobs();

            LibraryItemViewModel group = viewModel.Nodes.Last();
            Assert.Multiple(() =>
            {
                Assert.That(group.DisplayName, Is.EqualTo(LateGroupName));
                Assert.That(group.Children.Single().DisplayName, Is.EqualTo(LateName));
            });

            GraphNodeRegistry.RegisterNodes(LateGroupName).Add<LateSecondNode>(LateGroupName + " second").Register();
            Dispatcher.UIThread.RunJobs();
            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Nodes.Last(), Is.SameAs(group), "the group stays expanded when it grows");
                Assert.That(group.Children, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateNode), typeof(LateSecondNode)]);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.That(viewModel.Nodes, Is.EqualTo(before));
    }

    [AvaloniaTest]
    public async Task A_search_runs_again_when_a_node_is_registered()
    {
        using var viewModel = new LibraryTabViewModel(_library);
        await viewModel.Search(LateName, CancellationToken.None);
        Assert.That(viewModel.SearchResult, Is.Empty);
        _library.Register<LateItem>(LateFormat, LateName);

        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SearchResult.Any(item => item.DisplayName == LateName));
    }

    [AvaloniaTest]
    public void An_item_registered_into_a_group_goes_with_its_type()
    {
        using var viewModel = new LibraryTabViewModel(_library);
        LibraryItemViewModel[] before = [.. viewModel.LibraryItems];
        try
        {
            _library.RegisterGroup(LateGroupName, group => group.Add<LateItem>(LateFormat, LateName));
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
        using var viewModel = new LibraryTabViewModel(_library);
        await viewModel.Search(LateName, CancellationToken.None);
        Assert.That(viewModel.SearchResult, Is.Empty);
        try
        {
            _library.Register<LateItem>(LateFormat, LateName);

            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SearchResult.Any(item => item.DisplayName == LateName));
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateItem)]);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class LateItem;
}

internal sealed partial class LateNode : GraphNode;

internal sealed partial class LateSecondNode : GraphNode;
