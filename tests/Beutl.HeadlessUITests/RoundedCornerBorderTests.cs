using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Services;
using Beutl.Testing.Headless;
using Reactive.Bindings;
using Vector3 = System.Numerics.Vector3;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class RoundedCornerBorderTests
{
    private sealed class SolidThumbnailSource : IAnimatedThumbnailSource
    {
        public SolidThumbnailSource(Color color)
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(64, 36),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using (ILockedFramebuffer fb = bitmap.Lock())
            {
                unsafe
                {
                    byte* ptr = (byte*)fb.Address;
                    for (int y = 0; y < 36; y++)
                    {
                        byte* row = ptr + y * fb.RowBytes;
                        for (int x = 0; x < 64; x++)
                        {
                            row[x * 4 + 0] = color.B;
                            row[x * 4 + 1] = color.G;
                            row[x * 4 + 2] = color.R;
                            row[x * 4 + 3] = color.A;
                        }
                    }
                }
            }

            Still = bitmap;
        }

        public IImage? Still { get; }
        public AnimatedThumbnailFrames? Frames => null;
        public event EventHandler? Changed { add { } remove { } }
        public IDisposable RequestStill() => Disposable.Empty;
        public IDisposable RequestFrames() => Disposable.Empty;
    }

    [AvaloniaTest]
    public void Graph_node_keeps_its_border_visible_at_all_four_corners_when_expanded_and_collapsed()
    {
        using ThemeVariantScope themeScope = ThemeVariantScope.Use(ThemeVariant.Dark);

        var graph = new GraphModel();
        var node = new FactoryNode<Beutl.Media.Pen>();
        graph.Nodes.Add(node);
        using var vm = new NodeGraphViewModel(graph, NodeGraphTestContext.Create().Object);
        GraphNodeViewModel nodeVm = vm.Nodes.Single();
        nodeVm.IsSelected.Value = true;
        nodeVm.IsExpanded.Value = true;

        var view = new GraphNodeView
        {
            DataContext = nodeVm,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var window = new Window
        {
            Width = 360,
            Height = 480,
            Background = Brushes.Black,
            Content = view,
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);

            Assert.That(view.TryFindResource("FocusStrokeColorOuterBrush", ThemeVariant.Dark, out object? brushObj), Is.True);
            Color borderColor = ((ISolidColorBrush)brushObj!).Color;

            HeadlessCapture.SaveIfRequested(view, nameof(RoundedCornerBorderTests), "graph-node-expanded");
            AssertFourCornerBorders(window, view, borderColor, "expanded GraphNodeView");

            nodeVm.IsExpanded.Value = false;
            HeadlessTestHelpers.Render(4);
            HeadlessCapture.SaveIfRequested(view, nameof(RoundedCornerBorderTests), "graph-node-collapsed");
            AssertFourCornerBorders(window, view, borderColor, "collapsed GraphNodeView");
        }
        finally
        {
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Library_gallery_tile_keeps_its_border_visible_at_all_four_corners()
    {
        using ThemeVariantScope themeScope = ThemeVariantScope.Use(ThemeVariant.Dark);

        var thumbnail = new SolidThumbnailSource(Colors.Red);
        using var library = new LibraryTabViewModel(
            TestShell.FontManager,
            TestShell.Library,
            TestShell.Configuration.EditorConfig,
            NodeGraphTestContext.Create().Object);

        var gallery = new LibraryGalleryView(KnownLibraryItemFormats.FilterEffect)
        {
            DataContext = library,
            Margin = new Thickness(8),
        };
        var window = new Window
        {
            Width = 360,
            Height = 320,
            Background = Brushes.Black,
            Content = gallery,
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);

            Border frame = gallery.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("galleryFrame"));
            AnimatedThumbnail thumb = frame.GetVisualDescendants().OfType<AnimatedThumbnail>().Single();
            thumb.Source = thumbnail;

            window.MouseMove(HeadlessTestHelpers.Center(frame, window));
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render(4);

            Assert.That(frame.BorderBrush, Is.InstanceOf<ISolidColorBrush>());
            Color hoverBorderColor = ((ISolidColorBrush)frame.BorderBrush!).Color;
            HeadlessCapture.SaveIfRequested(frame, nameof(RoundedCornerBorderTests), "library-gallery-hover");
            AssertFourCornerBorders(window, frame, hoverBorderColor, "hovered LibraryGalleryView tile", overRedThumbnail: true);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Library_picker_tile_keeps_its_border_visible_at_all_four_corners()
    {
        using ThemeVariantScope themeScope = ThemeVariantScope.Use(ThemeVariant.Dark);

        var thumbnail = new SolidThumbnailSource(Colors.Red);
        var items = new ReactiveCollection<PinnableLibraryItem>
        {
            new("Blur", false, 0) { Thumbnail = thumbnail },
        };
        var presenter = new LibraryItemPickerFlyoutPresenter
        {
            Items = items,
            CanShowTiles = true,
            ShowTiles = true,
        };
        var window = new Window
        {
            Width = 420,
            Height = 520,
            Background = Brushes.Black,
            Content = presenter,
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);

            AnimatedThumbnail thumb = presenter.GetVisualDescendants().OfType<AnimatedThumbnail>().First();
            Border frame = thumb.GetVisualAncestors().OfType<Border>().First(b => b.BorderThickness.Left > 0);
            frame.BorderBrush = Brushes.Lime;
            HeadlessTestHelpers.Render(2);

            HeadlessCapture.SaveIfRequested(frame, nameof(RoundedCornerBorderTests), "library-picker-tile");
            AssertFourCornerBorders(window, frame, Colors.Lime, "LibraryItemPickerFlyoutPresenter tile", overRedThumbnail: true);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void AssertFourCornerBorders(
        Window window, Control control, Color expectedBorder, string description, bool overRedThumbnail = false)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        double scale = window.RenderScaling;
        Point origin = control.TranslatePoint(default, window)!.Value;
        double left = origin.X * scale;
        double top = origin.Y * scale;
        double right = (origin.X + control.Bounds.Width) * scale;
        double bottom = (origin.Y + control.Bounds.Height) * scale;

        var corners = new (string Name, double X, double Y)[]
        {
            ("top-left", left + 2.7 * scale, top + 2.7 * scale),
            ("top-right", right - 1 - 2.7 * scale, top + 2.7 * scale),
            ("bottom-left", left + 2.7 * scale, bottom - 1 - 2.7 * scale),
            ("bottom-right", right - 1 - 2.7 * scale, bottom - 1 - 2.7 * scale),
        };

        // The red thumbnail can contribute to the stroke's partially covered edge pixels.
        var borderRgb = new Vector3(overRedThumbnail ? 0 : expectedBorder.R, expectedBorder.G, expectedBorder.B);
        float borderLengthSquared = borderRgb.LengthSquared();
        var failures = new List<string>();
        foreach ((string name, double cx, double cy) in corners)
        {
            float bestDistance = float.PositiveInfinity;
            (int R, int G, int B) bestPixel = default;
            int ix = (int)Math.Round(cx);
            int iy = (int)Math.Round(cy);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var pixel = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(ix + dx, iy + dy));
                    var pixelRgb = new Vector3(overRedThumbnail ? 0 : pixel.R, pixel.G, pixel.B);
                    // Over the black window background, subpixel placement can make the stroke anywhere
                    // from partially covered to fully covered. Look for its color at 50% coverage or higher.
                    float coverage = Math.Clamp(Vector3.Dot(pixelRgb, borderRgb) / borderLengthSquared, 0.5f, 1f);
                    float distance = Vector3.Distance(pixelRgb, borderRgb * coverage);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestPixel = pixel;
                    }
                }
            }

            if (bestDistance > 32)
            {
                failures.Add($"{name} near ({ix},{iy}): closest pixel RGB{bestPixel}, expected ~RGB({expectedBorder.R},{expectedBorder.G},{expectedBorder.B})");
            }
        }

        Assert.That(failures, Is.Empty, $"Corner border missing on {description}:\n" + string.Join("\n", failures));
    }
}
