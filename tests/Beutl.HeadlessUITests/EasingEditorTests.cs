using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EasingEditorTests
{
    // EasingCurveEditor insets its plot so handles on the corners stay inside the bounds.
    private const double PlotInset = 8;

    [AvaloniaTest]
    [TestCase(EditorSurface.Property)]
    [TestCase(EditorSurface.Node)]
    [TestCase(EditorSurface.Settings)]
    [TestCase(EditorSurface.ListItem)]
    public void Easing_property_uses_the_easing_editor_in_every_context(EditorSurface surface)
    {
        var holder = new EasingHolder();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<Easing>(holder.Curve, holder), surface);
        using var _ = vm;

        Assert.That(vm, Is.TypeOf<EasingEditorViewModel<Easing>>());
        Assert.That(control, Is.TypeOf<EasingEditor>());
        var editor = (EasingEditor)control;
        Assert.Multiple(() =>
        {
            Assert.That(editor.Value, Is.SameAs(holder.Curve.CurrentValue));
            Assert.That(editor.Items!.Select(item => item.Type),
                Does.Contain(typeof(LinearEasing)).And.Contain(typeof(SplineEasing)).And.Contain(typeof(BounceEaseInOut)));
        });
    }

    [AvaloniaTest]
    public void Derived_easing_property_offers_only_assignable_easings()
    {
        var holder = new EasingHolder();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<SplineEasing>(holder.Spline, holder));
        using var _ = vm;

        Assert.That(vm, Is.TypeOf<EasingEditorViewModel<SplineEasing>>());
        Assert.That(((EasingEditor)control).Items!.Select(item => item.Type), Is.EqualTo(new[] { typeof(SplineEasing) }));
    }

    [AvaloniaTest]
    public void Choosing_an_easing_writes_a_new_instance_as_one_history_entry()
    {
        var holder = new EasingHolder();
        Easing original = holder.Curve.CurrentValue;
        var (vm, control) = CreateControl(new EnginePropertyAdapter<Easing>(holder.Curve, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        var editor = (EasingEditor)control;
        var window = new Window { Content = editor, Width = 480, Height = 320 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ComboBox comboBox = editor.GetVisualDescendants().OfType<ComboBox>().Single();
            Assert.That(SelectedType(comboBox), Is.EqualTo(typeof(LinearEasing)));
            EasingItem back = editor.Items!.Single(item => item.Type == typeof(BackEaseOut));

            comboBox.SelectedItem = back;
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.CurrentValue, Is.TypeOf<BackEaseOut>());
                Assert.That(holder.Curve.CurrentValue, Is.Not.SameAs(back.Preview),
                    "The item preview must not be shared with the property.");
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
            });

            Assert.That(history.History.Undo(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.CurrentValue, Is.SameAs(original));
                Assert.That(SelectedType(comboBox), Is.EqualTo(typeof(LinearEasing)));
                Assert.That(history.History.UndoCount, Is.EqualTo(0), "Following an undo must not record an edit.");
                Assert.That(history.History.RedoCount, Is.EqualTo(1));
            });

            Assert.That(history.History.Redo(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.That(SelectedType(comboBox), Is.EqualTo(typeof(BackEaseOut)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Dragging_a_spline_control_point_replaces_the_spline_as_one_history_entry()
    {
        var holder = new EasingHolder();
        var original = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        holder.Curve.CurrentValue = original;
        var (vm, control) = CreateControl(new EnginePropertyAdapter<Easing>(holder.Curve, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        var editor = (EasingEditor)control;
        var window = new Window { Content = editor, Width = 480, Height = 400 };
        try
        {
            window.Show();
            EasingCurveEditor curve = editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            WaitForHitTest(window, curve, start);

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.4, 0.3));

            var dragging = (SplineEasing)holder.Curve.CurrentValue;
            Assert.Multiple(() =>
            {
                Assert.That(dragging, Is.Not.SameAs(original));
                Assert.That(dragging.X1, Is.EqualTo(0.4).Within(0.01));
                Assert.That(dragging.Y1, Is.EqualTo(0.3).Within(0.01));
                Assert.That(curve.Easing, Is.SameAs(dragging));
                Assert.That(original.X1, Is.EqualTo(0.25f), "The displayed spline must not be mutated.");
            });

            Point end = ToWindow(window, curve, 0.5, 0.6);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            var edited = (SplineEasing)holder.Curve.CurrentValue;
            Assert.Multiple(() =>
            {
                Assert.That(edited.X1, Is.EqualTo(0.5).Within(0.01));
                Assert.That(edited.Y1, Is.EqualTo(0.6).Within(0.01));
                Assert.That(edited.X2, Is.EqualTo(0.25f));
                Assert.That(edited.Y2, Is.EqualTo(1f));
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
            });

            Assert.That(history.History.Undo(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.CurrentValue, Is.SameAs(original));
                Assert.That(curve.Easing, Is.SameAs(original));
                Assert.That(original.X1, Is.EqualTo(0.25f));
            });
            Assert.That(history.History.Redo(), Is.True);
            Assert.That(holder.Curve.CurrentValue, Is.SameAs(edited));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Curve_editor_holds_its_vertical_range_while_a_handle_leaves_the_unit_square()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        var curve = new EasingCurveEditor { Easing = spline };
        var edits = new List<EasingCurveEditedEventArgs>();
        var commits = new List<EasingCurveEditedEventArgs>();
        curve.Editing += (_, e) =>
        {
            edits.Add(e);
            curve.Easing = e.NewValue;
        };
        curve.Edited += (_, e) => commits.Add(e);
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            WaitForHitTest(window, curve, start);

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            // Positions are computed in the [0, 1] range that was shown when the drag began.
            window.MouseMove(ToWindow(window, curve, 0.3, 1.4));
            window.MouseMove(ToWindow(window, curve, 0.3, 1.2));
            Point end = ToWindow(window, curve, 0.3, 1.5);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);

            var edited = (SplineEasing)curve.Easing!;
            Assert.Multiple(() =>
            {
                Assert.That(edits, Has.Count.EqualTo(3));
                Assert.That(edits[1].NewValue.Y1, Is.EqualTo(1.2).Within(0.01));
                Assert.That(edited.Y1, Is.EqualTo(1.5).Within(0.01));
                Assert.That(commits, Has.Count.EqualTo(1));
                Assert.That(commits[0].OldValue, Is.SameAs(spline));
                Assert.That(commits[0].NewValue, Is.SameAs(edited));
                Assert.That(spline.Y1, Is.EqualTo(0.1f));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Read_only_curve_editor_ignores_handle_drags()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        var curve = new EasingCurveEditor { Easing = spline, IsReadOnly = true };
        int events = 0;
        curve.Editing += (_, _) => events++;
        curve.Edited += (_, _) => events++;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            WaitForHitTest(window, curve, start);

            Drag(window, start, ToWindow(window, curve, 0.6, 0.6));
            Assert.That(events, Is.Zero);

            // The same gesture edits once the editor is writable, so the drag above did reach the handle.
            curve.IsReadOnly = false;
            Drag(window, start, ToWindow(window, curve, 0.6, 0.6));
            Assert.That(events, Is.GreaterThan(0));
        }
        finally
        {
            window.Close();
        }
    }

    private static Type? SelectedType(ComboBox comboBox) => (comboBox.SelectedItem as EasingItem)?.Type;

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        HeadlessTestHelpers.Settle();
    }

    // Maps an easing coordinate inside the unit square, while the curve stays within [0, 1],
    // to a window position.
    private static Point ToWindow(Window window, EasingCurveEditor curve, double x, double y)
    {
        Rect plot = new Rect(curve.Bounds.Size).Deflate(PlotInset);
        var local = new Point(plot.X + x * plot.Width, plot.Bottom - y * plot.Height);
        return curve.TranslatePoint(local, window)!.Value;
    }

    // Avalonia's headless hit test answers only after the window has rendered a frame.
    private static void WaitForHitTest(Window window, Visual target, Point point)
    {
        for (int i = 0; i < 20; i++)
        {
            HeadlessTestHelpers.Render();
            if (window.InputHitTest(point) is Visual hit && (hit == target || target.IsVisualAncestorOf(hit)))
                return;
        }

        Assert.Fail("The curve editor never became hit-testable.");
    }

    private static (BaseEditorViewModel Vm, Control Control) CreateControl(IPropertyAdapter adapter, EditorSurface surface = EditorSurface.Property)
    {
        PropertyEditorExtension extension = PropertyEditorExtension.Instance;
        Assert.That(extension.MatchProperty([adapter]), Is.EqualTo(new[] { adapter }));
        IPropertyEditorContext? context;
        Control? control;
        bool createdContext = surface switch
        {
            EditorSurface.Node => extension.TryCreateContextForNode([adapter], out context),
            EditorSurface.Settings => extension.TryCreateContextForSettings([adapter], out context),
            EditorSurface.ListItem => extension.TryCreateContextForListItem(adapter, out context),
            _ => extension.TryCreateContext([adapter], out context)
        };
        Assert.That(createdContext, Is.True);
        bool createdControl;
        if (surface == EditorSurface.ListItem)
        {
            createdControl = extension.TryCreateControlForListItem(context!, out var listControl);
            control = listControl as Control;
        }
        else
        {
            createdControl = surface switch
            {
                EditorSurface.Node => extension.TryCreateControlForNode(context!, out control),
                EditorSurface.Settings => extension.TryCreateControlForSettings(context!, out control),
                _ => extension.TryCreateControl(context!, out control)
            };
        }
        Assert.That(createdControl, Is.True);
        control!.DataContext = context;
        return ((BaseEditorViewModel)context!, control);
    }

    public enum EditorSurface { Property, Node, Settings, ListItem }

    [SuppressResourceClassGeneration]
    public sealed class EasingHolder : EngineObject
    {
        public EasingHolder() => ScanProperties<EasingHolder>();

        public IProperty<Easing> Curve { get; } = Property.Create<Easing>(new LinearEasing());

        public IProperty<SplineEasing> Spline { get; } = Property.Create(new SplineEasing());
    }

    private sealed class HistoryScope : IDisposable
    {
        private readonly CoreObjectOperationObserver _observer;
        private readonly IDisposable _subscription;

        public HistoryScope(CoreObject target)
        {
            var sequence = new OperationSequenceGenerator();
            History = new HistoryManager(target, sequence);
            _observer = new CoreObjectOperationObserver(null, target, sequence);
            _subscription = History.Subscribe(_observer);
        }

        public HistoryManager History { get; }

        public void Dispose()
        {
            _subscription.Dispose();
            _observer.Dispose();
            History.Dispose();
        }
    }

    private sealed record Services(HistoryManager History) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type type) => type == typeof(HistoryManager) ? History : null;

        public void Visit(IPropertyEditorContext context) { }
    }
}
