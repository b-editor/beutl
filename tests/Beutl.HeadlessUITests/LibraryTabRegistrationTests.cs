using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Language;
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
    [TestCase("en-US", "perspective", typeof(CornerPin))]
    [TestCase("en-US", "four-point", typeof(CornerPin))]
    [TestCase("en-US", "CC Power Pin", typeof(CornerPin))]
    [TestCase("en-US", "animator", typeof(DelayAnimationEffect))]
    [TestCase("en-US", "range animator", typeof(RangeAnimatorEffect))]
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
    [TestCase("ja-JP", "4点", typeof(CornerPin))]
    [TestCase("ja-JP", "パース", typeof(CornerPin))]
    [TestCase("ja-JP", "テキストアニメーター", typeof(DelayAnimationEffect))]
    [TestCase("ja-JP", "範囲アニメーター", typeof(RangeAnimatorEffect))]
    [TestCase("es", "capa de ajuste", typeof(SourceBackdrop))]
    [TestCase("es", "adjustment", typeof(SourceBackdrop))]
    [TestCase("es", "spill", typeof(ChromaKey))]
    [TestCase("es", "perspectiva", typeof(CornerPin))]
    [TestCase("ko-KR", "조정 레이어", typeof(SourceBackdrop))]
    [TestCase("ko-KR", "adjustment", typeof(SourceBackdrop))]
    [TestCase("ko-KR", "posterize", typeof(SteppedMotion))]
    [TestCase("ko-KR", "원근", typeof(CornerPin))]
    [TestCase("zh-CN", "调整图层", typeof(SourceBackdrop))]
    [TestCase("zh-CN", "adjustment", typeof(SourceBackdrop))]
    [TestCase("zh-CN", "deform", typeof(Puppet))]
    [TestCase("zh-CN", "四点变形", typeof(CornerPin))]
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

    [AvaloniaTest, SetUICulture("en-US")]
    public void The_English_library_and_type_metadata_share_title_case_names()
    {
        LibraryRegistrar.RegisterAll(_library);
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
        (Type Type, string Name)[] names =
        [
            (typeof(ChromaKey), "Chroma Key"),
            (typeof(ColorKey), "Color Key"),
            (typeof(LumaKey), "Luma Key"),
            (typeof(LightRays), "Light Rays"),
            (typeof(MeshWarp), "Mesh Warp"),
            (typeof(MotionBlur), "Motion Blur"),
            (typeof(RangeAnimatorEffect), "Range Animator"),
            (typeof(RoundedRectShape), "Rounded Rectangle"),
            (typeof(PartsSplitEffect), "Split by Parts"),
            (typeof(SplitEffect), "Split Equally"),
            (typeof(WaveRefraction), "Wave Refraction"),
            (typeof(DrawableTimeController), "Time Controller"),
        ];

        Assert.Multiple(() =>
        {
            foreach ((Type type, string name) in names)
            {
                AssertLibraryAndTypeName(viewModel, type, name);
            }

            IEnumerable<string> libraryNames = viewModel.AllItems
                .Where(item => item.Data is SingleTypeLibraryItem or MultipleTypeLibraryItem)
                .Select(item => item.DisplayName);
            Assert.That(libraryNames, Is.SupersetOf(new[]
            {
                "Node Graph (Video)", "Node Graph (Effect)",
                "Scene Reference (Video)", "Scene Reference (Audio)",
            }));
        });
    }

    [AvaloniaTest]
    [TestCase("en-US", "3D (Experimental)", "Luma Color", "Sound", "Geometry", "Pen")]
    [TestCase("ja-JP", "3D（実験的）", "ルマカラー", "サウンド", "ジオメトリー", "ペン")]
    public void The_library_localizes_the_3D_group_luma_color_and_item_types(
        string cultureName, string groupName, string lumaColor, string sound, string geometry, string pen)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            LibraryRegistrar.RegisterAll(_library);
            _library.RegisterGroup(GraphicsStrings.LibraryGroup3D, group => group.Add<LateItem>(LateFormat, LateName));
            using var viewModel = new LibraryTabViewModel(TestShell.FontManager, _library, TestShell.Configuration.EditorConfig);
            LibraryItemViewModel group = viewModel.LibraryItems.Single(item => item.DisplayName == groupName);

            Assert.Multiple(() =>
            {
                Assert.That(group.Children.Select(item => item.DisplayName), Has.Member(GraphicsStrings.Scene3D));
                Assert.That(group.Children.Select(item => item.DisplayName), Has.Member(LateName));
                AssertLibraryAndTypeName(viewModel, typeof(LumaColor), lumaColor);
                Assert.That(TypeLabel(KnownLibraryItemFormats.Sound), Is.EqualTo(sound));
                Assert.That(TypeLabel(KnownLibraryItemFormats.Geometry), Is.EqualTo(geometry));
                Assert.That(TypeLabel(KnownLibraryItemFormats.Pen), Is.EqualTo(pen));
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }

        static string? TypeLabel(string format) => LibraryItemViewModel
            .CreateFromLibraryItem(new SingleTypeLibraryItem(format, typeof(LateItem), LateName)).Type;
    }

    private static void AssertLibraryAndTypeName(LibraryTabViewModel viewModel, Type type, string name)
    {
        LibraryItemViewModel item = viewModel.AllItems.Single(item =>
            item.TryDragDrop().Any(binding => binding.Item2 == type));
        Assert.That(item.DisplayName, Is.EqualTo(name), $"Library name for {type.Name}");
        Assert.That(type.GetCustomAttribute<DisplayAttribute>()?.GetName(), Is.EqualTo(name),
            $"Type display name for {type.Name}");
    }

    [AvaloniaTest]
    public async Task DipToWhite_is_omitted_from_the_library_and_default_picker()
    {
        LibraryRegistrar.RegisterAll(_library);
        var picker = new SelectLibraryItemDialogViewModel(TestShell.FontManager, _library,
            KnownLibraryItemFormats.ClipTransition, typeof(ClipTransition));
        try
        {
            await picker.SearchTask;
            HeadlessTestHelpers.Settle();

            IReadOnlySet<Type> registered = _library.GetTypesFromFormat(KnownLibraryItemFormats.ClipTransition);
            Type[] choices = picker.Items.Select(item => ((SingleTypeLibraryItem)item.UserData).ImplementationType).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(registered.Select(type => type.FullName),
                    Does.Not.Contain("Beutl.Graphics.Transitions.DipToWhiteTransition"));
                Assert.That(registered, Does.Contain(typeof(DipToColorTransition)));
                Assert.That(picker.ShowAll.Value, Is.False);
                Assert.That(choices, Is.EquivalentTo(registered));
            });
        }
        finally
        {
            picker.ReleaseThumbnails();
        }
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
