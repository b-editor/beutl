using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.Views;
using Beutl.Editor.Components.ColorScopesTab.Views.Scopes;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;
using Bitmap = Beutl.Media.Bitmap;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ColorScopesTabViewTests
{
    [AvaloniaTest]
    public void Only_the_attached_view_model_keeps_a_refresh_subscription()
    {
        using ColorScopesTabViewModel first = CreateViewModel();
        using ColorScopesTabViewModel second = CreateViewModel();
        var view = new ColorScopesTabView { DataContext = first };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Assert.That(RefreshSubscribers(first), Is.EqualTo(1));

            view.DataContext = second;
            Assert.That(RefreshSubscribers(first), Is.Zero, "The previous view model still reaches the view.");
            Assert.That(RefreshSubscribers(second), Is.EqualTo(1));

            view.DataContext = null;
            view.DataContext = second;
            Assert.That(RefreshSubscribers(second), Is.EqualTo(1), "Setting the same view model again doubles the refreshes.");

            window.Content = null;
            Assert.That(RefreshSubscribers(second), Is.Zero, "A detached view stays subscribed.");

            window.Content = view;
            Assert.That(RefreshSubscribers(second), Is.EqualTo(1), "A re-attached view stops refreshing.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(ColorScopeType.Waveform)]
    [TestCase(ColorScopeType.Waveform, WaveformMode.Luma)]
    [TestCase(ColorScopeType.Waveform, WaveformMode.RgbParade)]
    [TestCase(ColorScopeType.Histogram)]
    [TestCase(ColorScopeType.Histogram, WaveformMode.RgbOverlay, HistogramMode.Overlay)]
    [TestCase(ColorScopeType.Vectorscope)]
    [TestCase(ColorScopeType.FalseColor)]
    [TestCase(ColorScopeType.Zebra)]
    public async Task The_selected_scope_draws_the_preview_frame(
        ColorScopeType scopeType, WaveformMode waveformMode = WaveformMode.RgbOverlay,
        HistogramMode histogramMode = HistogramMode.Parade)
    {
        using var previewImage = new ReactivePropertySlim<Ref<Bitmap>?>();
        using var afterRendered = new Subject<Unit>();
        using var viewModel = CreateViewModel(previewImage, afterRendered);
        viewModel.IsSelected.Value = true;
        viewModel.SelectedScopeType.Value = scopeType;
        viewModel.WaveformMode.Value = waveformMode;
        viewModel.HistogramMode.Value = histogramMode;
        if (scopeType == ColorScopeType.Zebra)
        {
            viewModel.ColorSpace.Value = ScopeColorSpace.Linear;
        }

        using var source = Ref<Bitmap>.Create(CreatePreviewBitmap());
        var view = new ColorScopesTabView { DataContext = viewModel };
        var scope = view.FindControl<ScopeControlBase>(scopeType + "Control")!;
        var window = new Window { Width = 544, Height = 360, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            HeadlessTestHelpers.Render();
            Assert.That(scope.IsEffectivelyVisible, Is.True);
            if (scope is WaveformControl waveform)
                Assert.That(waveform.Mode, Is.EqualTo(waveformMode));
            if (scope is HistogramControl histogram)
                Assert.That(histogram.Mode, Is.EqualTo(histogramMode));

            previewImage.Value = source;
            afterRendered.OnNext(Unit.Default);
            // Start the posted refresh. The renderer releases its source clone after publishing the bitmap.
            HeadlessTestHelpers.Settle();
            await HeadlessTestHelpers.WaitUntilAsync(() => source.RefCount == 1,
                message: $"{scopeType} did not finish rendering the preview.");

            string captureName = scopeType switch
            {
                ColorScopeType.Waveform => $"{scopeType}-{waveformMode}",
                ColorScopeType.Histogram => $"{scopeType}-{histogramMode}",
                _ => scopeType.ToString()
            };
            HeadlessCapture.SaveIfRequested(view, nameof(ColorScopesTabViewTests), captureName);
            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            AssertScopePixels(frame, scope, scope.TranslatePoint(default, window)!.Value, source.Value);
        }
        finally
        {
            window.Close();
        }
    }

    private static Bitmap CreatePreviewBitmap()
    {
        const int bandWidth = 16;
        var bitmap = new Bitmap(bandWidth * 4, 32);
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<Bgra8888> row = bitmap.GetRow<Bgra8888>(y);
            row[..bandWidth].Fill(new Bgra8888(0, 0, 0, 255));
            row.Slice(bandWidth, bandWidth).Fill(new Bgra8888(128, 128, 128, 255));
            row.Slice(bandWidth * 2, bandWidth).Fill(new Bgra8888(255, 255, 255, 255));
            row[(bandWidth * 3)..].Fill(new Bgra8888(192, 64, 64, 255));
        }

        return bitmap;
    }

    private static void AssertScopePixels(WriteableBitmap frame, ScopeControlBase scope, Point origin, Bitmap source)
    {
        double margin = scope.AxisMargin;
        double plotWidth = scope.Bounds.Width - margin;
        double plotHeight = scope.Bounds.Height - margin;
        double imageScale = Math.Min(scope.Bounds.Width / source.Width, scope.Bounds.Height / source.Height);
        var image = new Rect(scope.Bounds.Size).CenterRect(new Rect(new Size(source.Width, source.Height) * imageScale));

        switch (scope)
        {
            case WaveformControl waveform:
                // The mid-gray band spans 1/4–1/2 of the input. In RGB Parade it repeats in each channel's section.
                double traceX = margin + plotWidth * (waveform.Mode == WaveformMode.RgbParade ? 0.125 : 0.375);
                Assert.That(Pixel(traceX, plotHeight / 2).R, Is.GreaterThan(100), "The mid-gray trace is missing.");
                Assert.That(Pixel(traceX, plotHeight / 4).R, Is.LessThan(50), "The trace should stay near mid-level.");
                break;
            case HistogramControl:
                Assert.That(Pixel(margin + plotWidth / 2, plotHeight / 6).R, Is.GreaterThan(100), "The mid-gray bin is empty.");
                Assert.That(Pixel(margin + plotWidth / 3, plotHeight / 6).R, Is.LessThan(20), "The input has no samples in this bin.");
                break;
            case VectorscopeControl:
                // The muted red band lands at Cb=113, Cr=192 in the 8-bit vectorscope.
                double size = Math.Min(plotWidth, plotHeight);
                double redX = margin + (plotWidth - size) / 2 + (int)(113 * (size - 1) / 255);
                double redY = (plotHeight - size) / 2 + (int)(63 * (size - 1) / 255);
                Assert.That(Pixel(redX, redY).R, Is.GreaterThan(80), "The red chroma sample is missing.");
                break;
            case FalseColorControl:
                AssertImagePixel(24, 16, [0, 216, 0], "Mid-gray should map to the green exposure band.");
                break;
            case ZebraControl:
                AssertImagePixel(6, 16, [255, 0, 0], "The black band should have a red stripe.");
                AssertImagePixel(24, 16, [128, 128, 128], "Mid-gray should retain its source color.");
                AssertImagePixel(34, 16, [0, 0, 0], "The white band should have a black stripe.");
                break;
        }

        (int R, int G, int B) Pixel(double x, double y) => HeadlessTestHelpers.ReadPixel(
            frame, new PixelPoint((int)(origin.X + x), (int)(origin.Y + y)));

        void AssertImagePixel(int x, int y, int[] expected, string message)
        {
            var (r, g, b) = Pixel(image.X + (x + 0.5) * imageScale, image.Y + (y + 0.5) * imageScale);
            // Scaling the image in the compositor can round the sampled channels by a few levels.
            Assert.That(new[] { r, g, b }, Is.EqualTo(expected).Within(3), message);
        }
    }

    private static ColorScopesTabViewModel CreateViewModel(
        ReactivePropertySlim<Ref<Bitmap>?>? previewImage = null, IObservable<Unit>? afterRendered = null)
    {
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(p => p.PreviewImage).Returns(previewImage ?? new ReactivePropertySlim<Ref<Bitmap>?>());
        player.SetupGet(p => p.AfterRendered).Returns(afterRendered ?? Observable.Never<Unit>());
        var editor = TestShell.CreateEditorContextMock();
        editor.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
        editor.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
        editor.Setup(e => e.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        return new ColorScopesTabViewModel(editor.Object);
    }

    private static int RefreshSubscribers(ColorScopesTabViewModel viewModel)
    {
        FieldInfo field = typeof(ColorScopesTabViewModel).GetField(
            nameof(ColorScopesTabViewModel.RefreshRequested), BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (field.GetValue(viewModel) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
