using System.Globalization;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
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

    [AvaloniaTest]
    [TestCase("en-US", "adjustment", typeof(SourceBackdrop))]
    [TestCase("en-US", "parent", typeof(DrawableGroup))]
    [TestCase("en-US", "null", typeof(DrawableGroup))]
    [TestCase("en-US", "mask", typeof(GeometryShape))]
    [TestCase("en-US", "matte", typeof(GeometryShape))]
    [TestCase("en-US", "green screen", typeof(ChromaKey))]
    [TestCase("en-US", "pixelate", typeof(MosaicEffect))]
    [TestCase("en-US", "precomp", typeof(SceneDrawable))]
    [TestCase("en-US", "remap", typeof(DrawableTimeController))]
    [TestCase("en-US", "wiggle", typeof(ShakeEffect))]
    [TestCase("en-US", "posterize", typeof(SteppedMotion))]
    [TestCase("en-US", "spill", typeof(ChromaKey))]
    [TestCase("en-US", "deform", typeof(Puppet))]
    [TestCase("en-US", "animator", typeof(DelayAnimationEffect))]
    [TestCase("en-US", "fade", typeof(OpacityEffect))]
    [TestCase("ja-JP", "調整レイヤー", typeof(SourceBackdrop))]
    [TestCase("ja-JP", "親", typeof(DrawableGroup))]
    [TestCase("ja-JP", "ヌル", typeof(DrawableGroup))]
    [TestCase("ja-JP", "adjustment", typeof(SourceBackdrop))]
    [TestCase("ja-JP", "parent", typeof(DrawableGroup))]
    [TestCase("ja-JP", "null", typeof(DrawableGroup))]
    [TestCase("ja-JP", "トラックマット", typeof(GeometryShape))]
    [TestCase("ja-JP", "グリーンバック", typeof(ChromaKey))]
    [TestCase("ja-JP", "ポスタリゼーション時間", typeof(SteppedMotion))]
    [TestCase("ja-JP", "posterize", typeof(SteppedMotion))]
    [TestCase("ja-JP", "スピル除去", typeof(ChromaKey))]
    [TestCase("ja-JP", "パペットピン", typeof(Puppet))]
    [TestCase("ja-JP", "テキストアニメーター", typeof(DelayAnimationEffect))]
    [TestCase("es", "capa de ajuste", typeof(SourceBackdrop))]
    [TestCase("es", "adjustment", typeof(SourceBackdrop))]
    [TestCase("es", "spill", typeof(ChromaKey))]
    [TestCase("ko-KR", "조정 레이어", typeof(SourceBackdrop))]
    [TestCase("ko-KR", "adjustment", typeof(SourceBackdrop))]
    [TestCase("ko-KR", "posterize", typeof(SteppedMotion))]
    [TestCase("zh-CN", "调整图层", typeof(SourceBackdrop))]
    [TestCase("zh-CN", "adjustment", typeof(SourceBackdrop))]
    [TestCase("zh-CN", "deform", typeof(Puppet))]
    public async Task Built_in_items_are_searchable_by_terms_from_other_editors(string cultureName, string query, Type expectedType)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            LibraryRegistrar.RegisterAll(_library);
            using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);

            await viewModel.Search(query, CancellationToken.None);

            IEnumerable<Type> resultTypes = viewModel.SearchResult
                .SelectMany(item => item.TryDragDrop())
                .Select(binding => binding.Item2);
            Assert.That(resultTypes, Has.Member(expectedType));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [AvaloniaTest]
    public void Every_built_in_item_has_a_description()
    {
        LibraryRegistrar.RegisterAll(_library);
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
        LibraryItemViewModel[] items = viewModel.AllItems
            .Where(item => item.Data is SingleTypeLibraryItem or MultipleTypeLibraryItem)
            .ToArray();

        Assert.That(items, Is.Not.Empty);
        Assert.That(items.Where(item => string.IsNullOrWhiteSpace(item.Description)).Select(item => item.FullDisplayName),
            Is.Empty, "Built-in items missing a description");
    }

    // A format of its own, so that no other part of the app picks up the item while it is registered.
    private const string LateFormat = "Beutl.HeadlessUITests.LateItem";

    // One word that no built-in item contains, since a search matches any of its words.
    private const string LateName = "Xyzzy";

    private const string LateGroupName = "Xyzzy group";

    [AvaloniaTest]
    public void An_item_registered_after_the_tab_opens_is_listed_until_its_type_unloads()
    {
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
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
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
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
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
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
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
        await viewModel.Search(LateName, CancellationToken.None);
        Assert.That(viewModel.SearchResult, Is.Empty);
        try
        {
            GraphNodeRegistry.RegisterNode<LateNode>(LateName);

            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.SearchResult.Any(item => item.DisplayName == LateName));
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(LateNode)]);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTest]
    public void An_item_registered_into_a_group_goes_with_its_type()
    {
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
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
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
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
