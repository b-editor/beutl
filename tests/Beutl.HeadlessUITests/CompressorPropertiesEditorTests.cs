using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.CompressorProperties.ViewModels;
using Beutl.Editor.Components.CompressorProperties.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class CompressorPropertiesEditorTests
{
    private static PropertyEditorExtension[] Extensions => LoadPrimitiveExtensionTask.PrimitiveExtensions
        .OfType<PropertyEditorExtension>().ToArray();

    // The gain computer as CompressorNode had it before the shared function; the curve must sample it.
    private static float OldGainReductionDb(float envelopeDb, float thresholdDb, float ratio, float kneeDb)
    {
        float slope = 1f - 1f / ratio;
        if (kneeDb > 0f)
        {
            float halfKnee = kneeDb * 0.5f;
            float diff = envelopeDb - thresholdDb;
            if (diff <= -halfKnee)
            {
                return 0f;
            }
            if (diff < halfKnee)
            {
                float x = diff + halfKnee;
                return slope * x * x / (2f * kneeDb);
            }
            return slope * diff;
        }

        return envelopeDb > thresholdDb ? slope * (envelopeDb - thresholdDb) : 0f;
    }

    [AvaloniaTest]
    public void CurveSamplesTheSameStaticGainAsTheNode()
    {
        using var host = new CompressorHost();
        float threshold = host.Effect.Threshold.Value;
        float ratio = host.Effect.Ratio.Value;
        float knee = host.Effect.Knee.Value;
        float makeup = host.Effect.MakeupGain.Value;

        for (int input = -60; input <= 0; input++)
        {
            float expected = input - OldGainReductionDb(input, threshold, ratio, knee) + makeup;
            Assert.That(host.Curve.OutputDb(input), Is.EqualTo(expected),
                $"input={input} dB");
        }
    }

    [AvaloniaTest]
    public void DraggingTheThresholdHandleChangesTheValueAndOneUndoRestoresIt()
    {
        using var host = new CompressorHost();
        NumberSliderEditor<float> box = host.NumberBox(host.ViewModel.ThresholdEditor);
        Point handle = host.Curve.ThresholdHandleCenter;
        double targetX = host.Curve.PlotBounds.Left + host.Curve.PlotBounds.Width / 3;

        host.Drag(handle, new Point(targetX, handle.Y));

        Assert.That(host.Effect.Threshold.Value, Is.EqualTo(-40f).Within(0.6f));
        Assert.That(box.Value, Is.EqualTo(host.Effect.Threshold.Value).Within(1e-4f),
            "The number box must show the dragged threshold.");
        Assert.That(host.ViewModel.Threshold.Value, Is.EqualTo(host.Effect.Threshold.Value).Within(1e-4f));
        Assert.That(host.History.UndoCount, Is.EqualTo(1), "One drag must be one undo step.");

        host.History.Undo();
        HeadlessTestHelpers.Render();

        Assert.That(host.Effect.Threshold.Value, Is.EqualTo(-20f).Within(1e-4f));
        Assert.That(box.Value, Is.EqualTo(-20f).Within(1e-4f),
            "Undo must reach the number box too.");
    }

    [AvaloniaTest]
    public void DraggingTheCurveChangesTheRatioAndClampsAtTheRangeEnds()
    {
        using var host = new CompressorHost();
        NumberSliderEditor<float> box = host.NumberBox(host.ViewModel.RatioEditor);

        Point start = host.Curve.PointOf(-5f);
        host.Drag(start, new Point(start.X, host.Curve.PlotBounds.Top));

        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(box.Value, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(host.Effect.Ratio.Value, Is.InRange(1f, 20f));
        Assert.That(host.History.UndoCount, Is.EqualTo(1), "One drag must be one undo step.");

        start = host.Curve.PointOf(-5f);
        host.Drag(start, new Point(start.X, host.Curve.PlotBounds.Bottom));

        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(20f).Within(1e-4f));
        Assert.That(box.Value, Is.EqualTo(20f).Within(1e-4f));
        Assert.That(host.Effect.Ratio.Value, Is.InRange(1f, 20f));
        Assert.That(host.History.UndoCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public void EditingTheNumberEditorMovesTheCurve()
    {
        using var host = new CompressorHost();

        // The number box writes through this preview, and the curve reads the same editor's shown value.
        host.ViewModel.RatioEditor!.PreviewEdit(10f, relative: false);
        HeadlessTestHelpers.Render();

        Assert.That(host.Curve.Ratio, Is.EqualTo(10f).Within(1e-4f));
        Assert.That(host.Curve.OutputDb(-10f), Is.EqualTo(-19f).Within(1e-4f));
    }

    [AvaloniaTest]
    public void DraggingTheCurveUsesTheRatioFromTheDragPoint()
    {
        using var host = new CompressorHost();
        Point start = host.Curve.PointOf(-5f);
        // out = -12 dB at in = -5 dB with threshold -20 dB gives ratio = 15 / 8.
        Point target = host.Curve.PointOf(-5f, -12f);

        host.Drag(start, target);

        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(1.875f).Within(1e-3f));
    }

    [AvaloniaTest]
    public void PressingAClippedCurveWithoutMovingKeepsTheRatioAndADragChangesItSmoothly()
    {
        // With threshold -10 dB and +12 dB makeup the curve above the threshold exceeds 0 dB, so the
        // plot clips it to the top edge. The ratio drag must use the real output, not the clipped edge.
        using var host = new CompressorHost(thresholdDb: -10f, makeupGainDb: 12f);
        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(4f));
        Point start = host.Curve.PointOf(-4f);

        host.Drag(start, start);
        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(4f).Within(1e-3f),
            "Pressing without moving must not change the ratio.");

        Point down = new(start.X, start.Y + 2);
        host.Drag(start, down);
        Assert.That(host.Effect.Ratio.Value, Is.GreaterThan(4f),
            "Dragging down must compress more; the old computation jumped straight to the range end.");
        Assert.That(host.Effect.Ratio.Value, Is.LessThan(20f));
    }

    [AvaloniaTest]
    public void DraggingAboveTheThresholdRaisesTheRatioFromOneToOne()
    {
        using var host = new CompressorHost(thresholdDb: -20f, kneeDb: 0f);
        host.Effect.Ratio.Value = 1f;
        HeadlessTestHelpers.Render();
        Assert.That(host.Curve.Ratio, Is.EqualTo(1f).Within(1e-4f));

        Point start = host.Curve.PointOf(-10f);
        host.Drag(start, new Point(start.X, start.Y + 4));

        Assert.That(host.Effect.Ratio.Value, Is.GreaterThan(1f));
        Assert.That(host.Effect.Ratio.Value, Is.LessThan(20f));
    }

    [AvaloniaTest]
    public void ReturningThePointerToTheStartRestoresTheRatioInOneUndoStep()
    {
        using var host = new CompressorHost();
        Point start = host.Curve.PointOf(-5f);
        Point down = new(start.X, start.Y + 4);
        Point startInWindow = host.Curve.TranslatePoint(start, host.Window)!.Value;
        Point downInWindow = host.Curve.TranslatePoint(down, host.Window)!.Value;

        host.Window.MouseMove(startInWindow);
        host.Window.MouseDown(startInWindow, MouseButton.Left);
        host.Window.MouseMove(downInWindow, RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render();
        Assert.That(host.Effect.Ratio.Value, Is.GreaterThan(4f));
        Assert.That(host.Effect.Ratio.Value, Is.LessThan(20f));

        host.Window.MouseMove(startInWindow, RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render();
        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(4f).Within(1e-4f),
            "Returning to the press height must restore the ratio the drag started from.");

        // A drag that ends where it started changes nothing, so it records nothing; going down again
        // shows the return stays inside the drag's single undo step.
        host.Window.MouseMove(downInWindow, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(downInWindow, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.That(host.History.UndoCount, Is.EqualTo(1),
            "The whole drag, including the return, is one undo step.");

        host.History.Undo();
        HeadlessTestHelpers.Render();
        Assert.That(host.Effect.Ratio.Value, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(host.History.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    public void DraggingTheKneeBelowTheThresholdChangesTheRatio()
    {
        // Threshold 0 with knee 24 compresses down to -12 dB, so the curve is visibly below the 1:1
        // diagonal at -6 dB even though it is below the threshold. The ratio drag must start there.
        using var host = new CompressorHost(thresholdDb: 0f, kneeDb: 24f);
        Point start = host.Curve.PointOf(-6f);
        Point diagonal = host.Curve.PointOf(-6f, -6f);
        Assert.That(start.Y, Is.GreaterThan(diagonal.Y), "The knee must visibly compress at -6 dB.");

        host.Drag(start, new Point(start.X, start.Y + 2));

        Assert.That(host.Effect.Ratio.Value, Is.GreaterThan(4f));
        Assert.That(host.Effect.Ratio.Value, Is.LessThanOrEqualTo(20f));
    }

    [AvaloniaTest]
    public void DraggingTheThresholdHandlePastBothEndsClampsToTheRange()
    {
        using var host = new CompressorHost();

        Point handle = host.Curve.ThresholdHandleCenter;
        host.Drag(handle, new Point(host.Curve.PlotBounds.Right, handle.Y));
        Assert.That(host.Effect.Threshold.Value, Is.EqualTo(0f).Within(1e-4f));

        handle = host.Curve.ThresholdHandleCenter;
        host.Drag(handle, new Point(host.Curve.PlotBounds.Left, handle.Y));
        Assert.That(host.Effect.Threshold.Value, Is.EqualTo(-60f).Within(1e-4f));
    }

    [AvaloniaTest]
    public void DraggingAnAnimatedThresholdEditsTheKeyFrameAtThePlayhead()
    {
        using var host = new CompressorHost(animateThreshold: true);
        var animation = (KeyFrameAnimation<float>)host.Effect.Threshold.GetKeyFrameAnimation()!;
        var first = (KeyFrame<float>)animation.KeyFrames[0];
        var second = (KeyFrame<float>)animation.KeyFrames[1];
        Point handle = host.Curve.ThresholdHandleCenter;
        double targetX = host.Curve.PlotBounds.Left + host.Curve.PlotBounds.Width / 2;

        host.Drag(handle, new Point(targetX, handle.Y));

        Assert.That(first.Value.Value, Is.EqualTo(-30f).Within(0.6f),
            "The drag must edit the keyframe at the playhead, not the property below the animation.");
        Assert.That(second.Value.Value, Is.EqualTo(-40f).Within(1e-4f));
        Assert.That(host.Curve.Threshold, Is.EqualTo(-30f).Within(0.6f));

        host.History.Undo();
        HeadlessTestHelpers.Render();

        Assert.That(first.Value.Value, Is.EqualTo(-20f).Within(1e-4f));
    }

    [AvaloniaTest]
    public void CurveFollowsThePlayheadWhenTheThresholdIsAnimated()
    {
        using var host = new CompressorHost(animateThreshold: true);

        Assert.That(host.Curve.Threshold, Is.EqualTo(-20f).Within(1e-4f));

        host.CurrentTime.Value = MediaTime.FromSeconds(1);
        HeadlessTestHelpers.Render();

        Assert.That(host.Curve.Threshold, Is.EqualTo(-40f).Within(1e-4f));
        Assert.That(host.Curve.OutputDb(-10f), Is.EqualTo(-32.5f).Within(1e-4f));
    }

    [AvaloniaTest]
    public void CurvePaintsWithTheThemeAndCapturesDarkAndLight()
    {
        using var host = new CompressorHost();
        using var scope = Assert.EnterMultipleScope();

        foreach (ThemeVariant theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            host.Window.RequestedThemeVariant = theme;
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(
                host.Window, string.Empty, $"compressor-curve-{theme.ToString().ToLowerInvariant()}");

            Rect area = host.Curve.PlotBounds;
            ThemePixels.AssertBrushOverWindow(
                host.Curve,
                new Point(area.X + area.Width * 0.5, area.Y + area.Height * 0.1),
                "ControlFillColorDefaultBrush");
            ThemePixels.AssertBrushOverWindow(
                host.Curve, host.Curve.ThresholdHandleCenter, "AccentTextFillColorPrimaryBrush");
            ThemePixels.AssertBrushOverWindow(
                host.Curve, host.Curve.PointOf(-5f), "AccentTextFillColorPrimaryBrush");
        }
    }

    private sealed class CompressorHost : IDisposable
    {
        private readonly List<IPropertyEditorContext> _contexts = [];
        private readonly PropertyEditorTestHost.HistoryScope _history;
        private readonly Mock<IEditorClock> _clock;

        public CompressorHost(
            bool animateThreshold = false, float thresholdDb = -20f, float kneeDb = 6f,
            float makeupGainDb = 0f)
        {
            Effect = new CompressorEffect();
            Effect.Threshold.Value = thresholdDb;
            Effect.Ratio.Value = 4f;
            Effect.Knee.Value = kneeDb;
            Effect.MakeupGain.Value = makeupGainDb;
            if (animateThreshold)
            {
                Effect.Threshold.SetKeyFrameAnimation(new KeyFrameAnimation<float>
                {
                    KeyFrames =
                    {
                        new KeyFrame<float>
                        {
                            KeyTime = { Value = MediaTime.Zero },
                            Value = { Value = -20f },
                        },
                        new KeyFrame<float>
                        {
                            KeyTime = { Value = MediaTime.FromSeconds(1) },
                            Value = { Value = -40f },
                        },
                    },
                });
            }

            _history = new PropertyEditorTestHost.HistoryScope(Effect);
            CurrentTime = new ReactivePropertySlim<MediaTime>(MediaTime.Zero);
            _clock = new Mock<IEditorClock>();
            _clock.SetupGet(clock => clock.CurrentTime).Returns(CurrentTime);

            PropertyEditorResolver.CreateContexts(
                PropertyAdapterFactory.CreateAdapters(Effect), Extensions, PropertyEditorSurface.Panel, _contexts.Add);
            ViewModel = _contexts.OfType<CompressorPropertiesViewModel>().Single();

            var factory = new Mock<IPropertyEditorFactory>();
            factory.Setup(f => f.CreateEditor(It.IsAny<IPropertyAdapter>()))
                .Returns((IPropertyAdapter property) =>
                    PropertyEditorResolver.CreateContext(property, Extensions, PropertyEditorSurface.Panel));
            ViewModel.Accept(new PropertyEditorTestHost.ServiceVisitor(
                (typeof(IPropertyEditorFactory), factory.Object),
                (typeof(HistoryManager), _history.History),
                (typeof(IEditorClock), _clock.Object)));

            Assert.That(PropertyEditorHost.TryCreateControl(
                ViewModel, PropertyEditorSurface.Panel, out Control? control), Is.True);
            Editor = (CompressorPropertiesEditor)control!;
            Editor.DataContext = ViewModel;
            Window = new Window
            {
                Content = new Border { Padding = new Thickness(12), Child = Editor },
                Width = 420,
                Height = 720,
                RequestedThemeVariant = ThemeVariant.Dark,
            };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Curve = Editor.GetVisualDescendants().OfType<CompressorCurveEditor>().Single();
        }

        public CompressorEffect Effect { get; }

        public CompressorPropertiesViewModel ViewModel { get; }

        public CompressorPropertiesEditor Editor { get; }

        public CompressorCurveEditor Curve { get; }

        public Window Window { get; }

        public ReactivePropertySlim<MediaTime> CurrentTime { get; }

        public HistoryManager History => _history.History;

        public NumberSliderEditor<float> NumberBox(NumberEditorViewModel<float>? editor)
        {
            Assert.That(editor, Is.Not.Null);
            return Editor.GetVisualDescendants().OfType<NumberSliderEditor<float>>()
                .Single(box => ReferenceEquals(box.DataContext, editor));
        }

        // Points are in the curve editor's coordinates.
        public void Drag(Point from, Point to)
        {
            Point start = Curve.TranslatePoint(from, Window)!.Value;
            Point end = Curve.TranslatePoint(to, Window)!.Value;
            Window.MouseMove(start);
            Window.MouseDown(start, MouseButton.Left);
            Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            _history.Dispose();
            CurrentTime.Dispose();
        }
    }
}
