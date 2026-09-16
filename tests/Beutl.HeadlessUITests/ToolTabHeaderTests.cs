using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabHeaderTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Dockable_title_follows_the_context_header()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-follows");

        var context = new FakeToolContext("first");
        var dockable = new BeutlToolDockable(context, editor);

        Assert.That(dockable.Title, Is.EqualTo("first"));

        context.HeaderSource.Value = "second";
        Assert.That(dockable.Title, Is.EqualTo("second"));

        // Keep the source alive so this verifies the dockable's unsubscribe.
        dockable.Dispose();
        context.HeaderSource.Value = "after-dispose";
        Assert.Multiple(() =>
        {
            Assert.That(context.HeaderSource.Value, Is.EqualTo("after-dispose"));
            Assert.That(dockable.Title, Is.EqualTo("second"));
        });

        context.HeaderSource.Dispose();
    }

    [AvaloniaTest]
    public async Task Blank_header_falls_back_to_the_extension_metadata()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-blank");

        var context = new FakeToolContext(string.Empty);
        using var dockable = new BeutlToolDockable(context, editor);

        Assert.That(dockable.Title, Is.EqualTo(FakeToolExtension.Instance.Header));

        context.HeaderSource.Value = "named";
        Assert.That(dockable.Title, Is.EqualTo("named"));

        context.HeaderSource.Value = string.Empty;
        Assert.That(dockable.Title, Is.EqualTo(FakeToolExtension.Instance.Header));
    }

    [AvaloniaTest]
    public async Task A_blank_extension_header_falls_back_to_the_display_name()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-blank-extension");

        var context = new FakeToolContext(string.Empty, BlankHeaderToolExtension.Instance);
        using var dockable = new BeutlToolDockable(context, editor);

        Assert.Multiple(() =>
        {
            Assert.That(dockable.Title, Is.EqualTo(BlankHeaderToolExtension.Instance.DisplayName));
            Assert.That(dockable.Title, Is.Not.EqualTo(BlankHeaderToolExtension.Instance.Name));
        });
    }

    [AvaloniaTest]
    public void The_dockable_icon_comes_from_its_extension()
    {
        var withIcon = new FakeToolContext("icon", IconToolExtension.Instance);
        using var iconDockable = new BeutlToolDockable(withIcon, null!);
        var withoutIcon = new FakeToolContext("plain");
        using var plainDockable = new BeutlToolDockable(withoutIcon, null!);

        Assert.Multiple(() =>
        {
            Assert.That((iconDockable.Icon as FASymbolIconSource)?.Symbol, Is.EqualTo(FASymbol.Accept));
            // FakeToolExtension leaves GetIcon at its default.
            Assert.That(plainDockable.Icon, Is.Null);
        });
    }

    [AvaloniaTest]
    public void Every_built_in_tool_tab_supplies_an_icon()
    {
        ToolTabExtension[] extensions = LoadPrimitiveExtensionTask.PrimitiveExtensions
            .OfType<ToolTabExtension>()
            .ToArray();

        Assert.That(extensions, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (ToolTabExtension extension in extensions)
            {
                Assert.That(extension.GetIcon(), Is.Not.Null, $"{extension.Name} should show an icon on its tab.");
            }
        });
    }

    [AvaloniaTest]
    public async Task The_tab_strip_draws_the_extension_icon_left_of_the_title()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-header-icon");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ToolTabStripItem libraryTab = FindTab(view, dockable => dockable.ToolContext.Extension is LibraryTabExtension);
            var libraryDockable = (BeutlToolDockable)libraryTab.DataContext!;
            Viewbox slot = FindIconSlot(libraryTab);
            FAIconSourceElement icon = slot.GetVisualDescendants().OfType<FAIconSourceElement>().Single();
            TextBlock title = libraryTab.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Name == "PART_TabTitle");

            var size = (double)slot.FindResource("DockToolTabIconSize")!;
            // A glyph source draws at its own font size, so measure what reaches the tab rather than
            // the element's own bounds: an unscaled icon overflows the slot instead of shrinking.
            Rect drawn = new Rect(icon.Bounds.Size).TransformToAABB(icon.TransformToVisual(libraryTab)!.Value);

            Assert.Multiple(() =>
            {
                Assert.That(libraryDockable.Icon, Is.Not.Null);
                Assert.That(icon.IconSource, Is.SameAs(libraryDockable.Icon));
                Assert.That(slot.IsVisible, Is.True);
                Assert.That(drawn.Width, Is.EqualTo(size).Within(0.01));
                Assert.That(drawn.Height, Is.EqualTo(size).Within(0.01));
                Assert.That(
                    drawn.Right,
                    Is.LessThanOrEqualTo(title.TranslatePoint(default, libraryTab)!.Value.X));
            });

            // A tool without an icon keeps its title against the tab's left edge.
            var withoutIcon = new FakeToolContext("no icon");
            Assert.That(editor.OpenToolTab(withoutIcon), Is.True);
            HeadlessTestHelpers.Render();

            Viewbox plainSlot = FindIconSlot(FindTab(view, dockable => ReferenceEquals(dockable.ToolContext, withoutIcon)));
            Assert.Multiple(() =>
            {
                Assert.That(plainSlot.IsVisible, Is.False);
                Assert.That(plainSlot.Bounds.Width, Is.EqualTo(0));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_tab_icon_is_drawn_no_larger_than_its_slot()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("tooltab-icon-extent");

        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ToolTabStripItem tab = FindTab(view, dockable => dockable.ToolContext.Extension is LibraryTabExtension);
            Viewbox slot = FindIconSlot(tab);
            TextBlock title = tab.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Name == "PART_TabTitle");

            Point tabOrigin = tab.TranslatePoint(default, window)!.Value;
            double titleLeft = title.TranslatePoint(default, window)!.Value.X;
            // Everything left of the title, minus the selected tab's accent underline.
            var region = new Rect(
                tabOrigin.X,
                tabOrigin.Y,
                titleLeft - tabOrigin.X,
                Math.Max(0, tab.Bounds.Height - AccentUnderlineHeight));

            Rect ink = MeasureInk(window, region);

            Assert.Multiple(() =>
            {
                Assert.That(ink.Width, Is.GreaterThan(0), "The tab should draw an icon left of its title.");
                Assert.That(ink.Width, Is.LessThanOrEqualTo(slot.Bounds.Width + 1));
                Assert.That(ink.Height, Is.LessThanOrEqualTo(slot.Bounds.Height + 1));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static ToolTabStripItem FindTab(Visual root, Func<BeutlToolDockable, bool> predicate)
    {
        return root.GetVisualDescendants()
            .OfType<ToolTabStripItem>()
            .Single(item => item.DataContext is BeutlToolDockable dockable && predicate(dockable));
    }

    private static Viewbox FindIconSlot(ToolTabStripItem tab)
    {
        return tab.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(presenter => presenter.Name == "PART_IconPresenter")
            .GetVisualDescendants()
            .OfType<Viewbox>()
            .Single();
    }

    private const int AccentUnderlineHeight = 3;

    // The tab background renders at 249 and the icon's strokes well below 235, so anything darker
    // than this is drawn content rather than the tab itself.
    private const int InkThreshold = 235;

    // Measures what the icon actually paints. Its element bounds cannot stand in for this: a glyph
    // source draws at its own font size and overflows a smaller element instead of shrinking to it,
    // so only the rendered pixels show whether the slot really sizes the icon.
    private static Rect MeasureInk(Window window, Rect region)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("CaptureRenderedFrame returned null; the window never rendered.");
        using ILockedFramebuffer buffer = frame.Lock();
        Assert.That(buffer.Format, Is.EqualTo(PixelFormat.Rgba8888).Or.EqualTo(PixelFormat.Bgra8888),
            $"MeasureInk assumes a 32bpp RGBA or BGRA framebuffer but the frame is {buffer.Format}.");

        double scale = window.RenderScaling;
        int left = Math.Max(0, (int)Math.Floor(region.X * scale));
        int top = Math.Max(0, (int)Math.Floor(region.Y * scale));
        int right = Math.Min(buffer.Size.Width, (int)Math.Ceiling(region.Right * scale));
        int bottom = Math.Min(buffer.Size.Height, (int)Math.Ceiling(region.Bottom * scale));

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        unsafe
        {
            for (int y = top; y < bottom; y++)
            {
                uint* row = (uint*)((byte*)buffer.Address + (y * buffer.RowBytes));
                for (int x = left; x < right; x++)
                {
                    uint pixel = row[x];
                    // A mean over the three colour channels is the same whichever order they came in.
                    int mean = (int)(((pixel & 0xFF) + ((pixel >> 8) & 0xFF) + ((pixel >> 16) & 0xFF)) / 3);
                    if (mean >= InkThreshold) continue;

                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return minX == int.MaxValue
            ? default
            : new Rect(minX / scale, minY / scale, (maxX - minX + 1) / scale, (maxY - minY + 1) / scale);
    }

    private sealed class FakeToolContext(string header, ToolTabExtension? extension = null) : IToolContext
    {
        public ReactivePropertySlim<string> HeaderSource { get; } = new(header);

        public ToolTabExtension Extension { get; } = extension ?? FakeToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header => HeaderSource;

        // Keep HeaderSource alive to test the dockable's unsubscribe.
        public void Dispose()
        {
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class BlankHeaderToolExtension : ToolTabExtension
    {
        public static readonly BlankHeaderToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "BlankHeaderToolTab";

        public override string DisplayName => "Blank header tool tab";

        public override string? Header => "   ";

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext(string.Empty, Instance);
            return true;
        }
    }

    private sealed class IconToolExtension : ToolTabExtension
    {
        public static readonly IconToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "IconToolTab";

        public override string DisplayName => "Icon tool tab";

        public override string? Header => "Icon tool tab";

        public override FAIconSource? GetIcon() => new FASymbolIconSource { Symbol = FASymbol.Accept };

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext(string.Empty, Instance);
            return true;
        }
    }

    private sealed class FakeToolExtension : ToolTabExtension
    {
        public static readonly FakeToolExtension Instance = new();

        public override bool CanMultiple => true;

        public override string Name => "FakeToolTab";

        public override string DisplayName => "Fake tool tab";

        public override string? Header => "Fake tool tab";

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new FakeToolContext("Fake tool tab");
            return true;
        }
    }
}
