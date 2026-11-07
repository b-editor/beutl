using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.Views;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Moq;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaPoint = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ColorGradingTabTests
{
    public enum DragEnd { CaptureLoss, Playback, Escape, Undo }

    // Cb and Cr as the vectorscope plots them (BT.709).
    private static Vector ScopeChroma(GradingColor c)
        => new((-0.11457 * c.R) - (0.38543 * c.G) + (0.5 * c.B), (0.5 * c.R) - (0.45415 * c.G) - (0.04585 * c.B));

    [Test]
    [TestCase(1f, 0f, 0f)]
    [TestCase(0f, 1f, 0f)]
    [TestCase(0f, 0f, 1f)]
    [TestCase(0.05f, 0f, -0.05f)]
    [TestCase(-0.2f, 0.3f, 0.1f)]
    public void The_chroma_follows_the_vectorscope_axes(float r, float g, float b)
    {
        var offset = new GradingColor(r, g, b);
        Vector chroma = ColorBalance.GetChroma(offset + GradingColor.One, GradingColor.One);
        Vector expected = ScopeChroma(offset);
        Assert.Multiple(() =>
        {
            Assert.That(chroma.X, Is.EqualTo(expected.X).Within(1e-4));
            Assert.That(chroma.Y, Is.EqualTo(expected.Y).Within(1e-4));
        });
    }

    [Test]
    public void A_chroma_shift_keeps_the_luma_and_splitting_a_color_loses_nothing()
    {
        var random = new Random(4242);
        for (int i = 0; i < 200; i++)
        {
            var neutral = new GradingColor(Next(), Next(), Next());
            var color = new GradingColor(Next(), Next(), Next());
            Vector chroma = ColorBalance.GetChroma(color, neutral);
            GradingColor gray = ColorBalance.WithoutChroma(color, neutral);
            GradingColor rebuilt = gray + ColorBalance.ChromaOffset(chroma);
            GradingColor grayOffset = gray - neutral;
            Assert.Multiple(() =>
            {
                Assert.That(ColorBalance.Luma(ColorBalance.ChromaOffset(chroma)), Is.EqualTo(0).Within(1e-5));
                Assert.That(rebuilt.R, Is.EqualTo(color.R).Within(1e-5));
                Assert.That(rebuilt.G, Is.EqualTo(color.G).Within(1e-5));
                Assert.That(rebuilt.B, Is.EqualTo(color.B).Within(1e-5));
                Assert.That(grayOffset.G, Is.EqualTo(grayOffset.R).Within(1e-5));
                Assert.That(grayOffset.B, Is.EqualTo(grayOffset.R).Within(1e-5));
            });
        }

        float Next() => (float)((random.NextDouble() * 2) - 1);
    }

    [AvaloniaTest]
    public void Dragging_the_puck_right_pushes_gain_toward_blue_and_up_toward_red_keeping_the_luma()
    {
        using var host = new WheelHost(effect => effect.Gain);
        AvaPoint start = host.PadCenter();
        host.Drag(start, start + new Vector(30, 0));
        GradingColor right = host.Property.Value;
        Vector chroma = ColorBalance.GetChroma(right, GradingColor.One);
        Assert.Multiple(() =>
        {
            Assert.That(right.B, Is.GreaterThan(1f));
            Assert.That(chroma.X, Is.GreaterThan(0.01));
            Assert.That(chroma.Y, Is.EqualTo(0).Within(1e-6));
            Assert.That(ColorBalance.Luma(right - GradingColor.One), Is.EqualTo(0).Within(1e-6));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
        });

        // Start a little off the last press, which would otherwise count as a double click.
        AvaPoint next = host.PadCenter() + new Vector(6, 0);
        host.Drag(next, next + new Vector(0, -30));
        GradingColor up = host.Property.Value;
        Assert.Multiple(() =>
        {
            Assert.That(up.R, Is.GreaterThan(right.R));
            Assert.That(ColorBalance.GetChroma(up, GradingColor.One).Y, Is.GreaterThan(0.01));
            Assert.That(ColorBalance.Luma(up - GradingColor.One), Is.EqualTo(0).Within(1e-6));
            Assert.That(host.History.UndoCount, Is.EqualTo(2));
        });
    }

    [AvaloniaTest]
    [TestCase(DragEnd.CaptureLoss)]
    [TestCase(DragEnd.Playback)]
    [TestCase(DragEnd.Escape)]
    [TestCase(DragEnd.Undo)]
    public void A_puck_drag_keeps_on_capture_loss_and_playback_and_reverts_on_Escape_and_Undo(DragEnd end)
    {
        using var host = new WheelHost(effect => effect.Lift);
        IPointer? pointer = null;
        host.Pad.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AvaPoint start = host.PadCenter();
        host.Window.MouseMove(start);
        host.Window.MouseDown(start, MouseButton.Left);
        host.Window.MouseMove(start + new Vector(30, 0), RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render();
        GradingColor reached = host.Property.Value;
        Assert.That(reached, Is.Not.EqualTo(GradingColor.Zero));
        Assert.That(host.History.IsEditing, Is.True, "The drag must hold an edit session.");

        switch (end)
        {
            case DragEnd.CaptureLoss:
                pointer!.Capture(null);
                break;
            case DragEnd.Playback:
                host.Playing.Value = true;
                host.Playing.Value = false;
                break;
            case DragEnd.Escape:
                host.Window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                host.Window.KeyRelease(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
                break;
            case DragEnd.Undo:
                Assert.That(host.History.TryCancelGesture(), Is.True);
                break;
        }

        HeadlessTestHelpers.Render();
        Assert.That(host.History.IsEditing, Is.False, "The end closes the session before any release.");
        host.Window.MouseMove(start + new Vector(60, 0), RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(start + new Vector(60, 0), MouseButton.Left);
        HeadlessTestHelpers.Render();

        bool kept = end is DragEnd.CaptureLoss or DragEnd.Playback;
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(kept ? reached : GradingColor.Zero));
            Assert.That(host.History.UndoCount, Is.EqualTo(kept ? 1 : 0));
            Assert.That(host.History.IsEditing, Is.False);
        });
    }

    // The old wheel turned a color into hue, saturation and value, which made every channel negative when one was.
    [AvaloniaTest]
    public void The_jog_moves_r_g_and_b_alike_and_keeps_a_color_of_mixed_signs()
    {
        using var host = new WheelHost(effect => effect.Shadows, new GradingColor(0.05f, 0f, -0.05f));
        GradingWheel jog = host.Wheel.GetVisualDescendants().OfType<GradingWheel>().Single();
        AvaPoint start = HeadlessTestHelpers.Center(jog, host.Window);
        host.Drag(start, start + new Vector(40, 0));
        GradingColor value = host.Property.Value;
        float delta = value.R - 0.05f;
        Assert.Multiple(() =>
        {
            Assert.That(delta, Is.GreaterThan(0f));
            Assert.That(value.G, Is.EqualTo(delta).Within(1e-6));
            Assert.That(value.B, Is.EqualTo(-0.05f + delta).Within(1e-6));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void The_reset_button_returns_gain_to_one_in_one_step()
    {
        using var host = new WheelHost(effect => effect.Gain, new GradingColor(1.1f, 1.02f, 0.95f));
        Button reset = host.Wheel.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_ResetButton");
        Assert.That(reset.IsEnabled, Is.True);
        HeadlessTestHelpers.Click(reset);
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(GradingColor.One));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
            Assert.That(reset.IsEnabled, Is.False, "Nothing is left to reset.");
        });
    }

    [AvaloniaTest]
    public void A_double_click_on_the_pad_removes_only_the_chroma()
    {
        var start = new GradingColor(0.05f, 0f, -0.05f);
        using var host = new WheelHost(effect => effect.Lift, start);
        AvaPoint center = host.PadCenter() + new Vector(10, 10);
        host.Window.MouseDown(center, MouseButton.Left);
        host.Window.MouseUp(center, MouseButton.Left);
        host.Window.MouseDown(center, MouseButton.Left);
        host.Window.MouseUp(center, MouseButton.Left);
        HeadlessTestHelpers.Render();
        GradingColor value = host.Property.Value;
        float luma = ColorBalance.Luma(start);
        Assert.Multiple(() =>
        {
            Assert.That(value.R, Is.EqualTo(luma).Within(1e-6));
            Assert.That(value.G, Is.EqualTo(luma).Within(1e-6));
            Assert.That(value.B, Is.EqualTo(luma).Within(1e-6));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void A_slider_spans_the_range_from_the_default_and_a_double_click_returns_there()
    {
        var effect = new ColorGrading();
        using var history = new HistoryScope(effect);
        using var viewModel = new NumberEditorViewModel<float>(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)effect.ContrastPivot, effect));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new NumberSliderEditor<float> { Theme = (ControlTheme)Application.Current!.FindResource("NumberSliderEditorTheme")! };
        PropertyEditorControls.Attach(viewModel, editor);
        var window = new Window { Content = editor, Width = 420, Height = 80 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(editor.Minimum, Is.Zero);
                Assert.That(editor.Maximum, Is.EqualTo(1));
                Assert.That(editor.Origin, Is.EqualTo(0.5));
            });

            ValueSlider slider = editor.GetVisualDescendants().OfType<ValueSlider>().Single();
            double x = ValueSlider.ThumbRadius + ((slider.Bounds.Width - (2 * ValueSlider.ThumbRadius)) * 0.25);
            AvaPoint quarter = slider.TranslatePoint(new AvaPoint(x, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(quarter, MouseButton.Left);
            window.MouseUp(quarter, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(effect.ContrastPivot.Value, Is.EqualTo(0.25f).Within(0.006));
                Assert.That(effect.ContrastPivot.Value * 100, Is.EqualTo(MathF.Round(effect.ContrastPivot.Value * 100)).Within(1e-3),
                    "The slider moves in steps of the property's small change.");
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
            });

            // The press right after the last one at the same point is the second of a double click.
            window.MouseDown(quarter, MouseButton.Left);
            window.MouseUp(quarter, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(effect.ContrastPivot.Value, Is.EqualTo(0.5f));
                Assert.That(history.History.UndoCount, Is.EqualTo(2));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Reset_all_returns_every_property_in_one_undo_step()
    {
        (EditViewModel editor, ColorGrading grading) = await OpenGradingAsync("grading-reset-all");
        editor.HistoryManager.ExecuteInTransaction(() =>
        {
            grading.Temperature.Value = 20;
            grading.Exposure.Value = 1;
            grading.Gain.Value = new GradingColor(1.2f, 1f, 0.9f);
            grading.LowRange.Value = 25;
        }, "Setup");
        int before = editor.HistoryManager.UndoCount;
        using var model = new ColorGradingTabViewModel(editor);
        model.Effect.Value = grading;

        model.ResetAll();
        Assert.Multiple(() =>
        {
            Assert.That(grading.Temperature.Value, Is.Zero);
            Assert.That(grading.Exposure.Value, Is.Zero);
            Assert.That(grading.Gain.Value, Is.EqualTo(GradingColor.One));
            Assert.That(grading.LowRange.Value, Is.EqualTo(40));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        });

        Assert.That(await editor.UndoAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(grading.Temperature.Value, Is.EqualTo(20));
            Assert.That(grading.Gain.Value, Is.EqualTo(new GradingColor(1.2f, 1f, 0.9f)));
            Assert.That(grading.LowRange.Value, Is.EqualTo(25));
        });
    }

    [AvaloniaTest]
    public async Task The_tonal_range_handles_edit_the_ranges_and_stop_at_each_other()
    {
        (EditViewModel editor, ColorGrading grading) = await OpenGradingAsync("grading-tonal-range");
        int before = editor.HistoryManager.UndoCount;
        using var model = new ColorGradingTabViewModel(editor);
        model.Effect.Value = grading;
        model.WheelMode.Value = ColorGradingWheelMode.ShadowsMidtonesHighlights;
        var view = new ColorGradingTabView { DataContext = model };
        var window = new Window { Content = view, Width = 960, Height = 640 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TonalRangeSlider range = view.FindControl<TonalRangeSlider>("TonalRangeSlider")!;
            Assert.That((range.Low, range.High), Is.EqualTo((40.0, 60.0)));

            AvaPoint low = range.TranslatePoint(new AvaPoint(range.XOf(40), 18), window)!.Value;
            AvaPoint far = range.TranslatePoint(new AvaPoint(range.XOf(90), 18), window)!.Value;
            window.MouseDown(low, MouseButton.Left);
            window.MouseMove(far, RawInputModifiers.LeftMouseButton);
            window.MouseUp(far, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(grading.LowRange.Value, Is.EqualTo(60), "The low handle stops at the high one.");
                Assert.That(grading.HighRange.Value, Is.EqualTo(60));
                Assert.That(range.Low, Is.EqualTo(60));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A tab docked at the bottom is wide and low: the sliders stand in a column beside the wheels, which keep one row
    // as the tonal wheels do. Elsewhere the sliders stay above. In every shape the wheels stay inside the tab.
    [AvaloniaTest]
    [TestCase(1080, 360, 1, true, true)]
    [TestCase(1080, 360, 0, true, true)]
    [TestCase(1920, 360, 1, true, true)]
    [TestCase(420, 900, 0, false, false)]
    [TestCase(560, 720, 1, false, false)]
    public async Task The_wheels_and_sliders_fit_the_tab(int width, int height, int mode, bool slidersBeside, bool oneRow)
    {
        (EditViewModel editor, ColorGrading grading) = await OpenGradingAsync($"grading-layout-{width}");
        using var model = new ColorGradingTabViewModel(editor);
        model.Effect.Value = grading;
        model.WheelMode.Value = model.AvailableWheelModes[mode];
        var view = new ColorGradingTabView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ColorWheelsPanel wheels = view.GetVisualDescendants().OfType<ColorWheelsPanel>().Single(panel => panel.IsEffectivelyVisible);
            ColorGradingWheel[] shown = [.. wheels.Children.OfType<ColorGradingWheel>()];
            GradingAdjustmentsPanel groups = view.GetVisualDescendants().OfType<GradingAdjustmentsPanel>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(view.IsAdjustmentsBeside, Is.EqualTo(slidersBeside));
                if (slidersBeside)
                    Assert.That(groups.UsedColumns, Is.EqualTo(1), "Beside the wheels, the groups stack.");
                if (oneRow)
                    Assert.That(wheels.Columns, Is.EqualTo(shown.Length), "The wheels keep one row.");
            });

            foreach (ColorGradingWheel wheel in shown)
            {
                ColorBalancePad pad = wheel.GetVisualDescendants().OfType<ColorBalancePad>().Single();
                AvaPoint bottomRight = wheel.TranslatePoint(new AvaPoint(wheel.Bounds.Width, wheel.Bounds.Height), view)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(pad.DiskBounds.Width, Is.GreaterThanOrEqualTo(80), wheel.Header);
                    Assert.That(bottomRight.X, Is.LessThanOrEqualTo(view.Bounds.Width + 0.5), wheel.Header);
                    Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(view.Bounds.Height + 0.5), wheel.Header);
                });
            }

            foreach (ValueSlider slider in view.GetVisualDescendants().OfType<ValueSlider>())
                Assert.That(slider.Bounds.Width, Is.GreaterThanOrEqualTo(48));
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<(EditViewModel Editor, ColorGrading Grading)> OpenGradingAsync(string name)
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(name);
        var grading = new ColorGrading();
        var shape = new RectShape();
        shape.FilterEffect.Value = grading;
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        return (editor, grading);
    }

    // A wheel on one property of a ColorGrading, with a history and a player that can start.
    private sealed class WheelHost : IDisposable
    {
        private readonly HistoryScope _history;
        private readonly GradingColorEditorViewModel _viewModel;

        public WheelHost(Func<ColorGrading, IProperty<GradingColor>> pick, GradingColor? start = null)
        {
            var effect = new ColorGrading();
            Property = pick(effect);
            if (start is { } value)
                Property.Value = value;

            _history = new HistoryScope(effect);
            var player = new Mock<IPreviewPlayer>();
            player.SetupGet(x => x.IsPlaying).Returns(Playing);
            _viewModel = new GradingColorEditorViewModel(
                new AnimatablePropertyAdapter<GradingColor>((AnimatableProperty<GradingColor>)Property, effect));
            _viewModel.Accept(new ServiceVisitor((typeof(HistoryManager), _history.History), (typeof(IPreviewPlayer), player.Object)));
            Wheel = new ColorGradingWheel();
            _viewModel.Accept(Wheel);
            Window = new Window { Content = Wheel, Width = 260, Height = 380 };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Pad = Wheel.GetVisualDescendants().OfType<ColorBalancePad>().Single();
        }

        public IProperty<GradingColor> Property { get; }

        public HistoryManager History => _history.History;

        public ReactivePropertySlim<bool> Playing { get; } = new(false);

        public ColorGradingWheel Wheel { get; }

        public ColorBalancePad Pad { get; }

        public Window Window { get; }

        public AvaPoint PadCenter() => Pad.TranslatePoint(Pad.DiskBounds.Center, Window)!.Value;

        public void Drag(AvaPoint from, AvaPoint to)
        {
            Window.MouseMove(from);
            Window.MouseDown(from, MouseButton.Left);
            Window.MouseMove(from + ((to - from) / 2), RawInputModifiers.LeftMouseButton);
            Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Window.MouseUp(to, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            Window.Close();
            _viewModel.Dispose();
            _history.Dispose();
        }
    }
}
