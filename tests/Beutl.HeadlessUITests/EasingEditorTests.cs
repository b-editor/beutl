using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EasingEditorTests
{
    // EasingCurveEditor insets its plot so handles on the corners stay inside the bounds.
    private const double PlotInset = 12;

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
            Assert.That(editor.Value, Is.SameAs(holder.Curve.Value));
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

    [Test]
    public void Registered_easings_follow_the_built_in_ones_with_their_display_names()
    {
        EasingItem[] items = EasingEditorItems.Create(
            typeof(Easing),
            [typeof(DisplayedEasing), typeof(AbstractEasing), typeof(ParameterizedEasing), typeof(ThrowingEasing), typeof(LinearEasing)],
            _ => null);
        EasingItem[] splines = EasingEditorItems.Create(
            typeof(SplineEasing), [typeof(DisplayedEasing), typeof(DerivedSpline)], _ => null);

        Assert.Multiple(() =>
        {
            Assert.That(items[^1].Type, Is.EqualTo(typeof(DisplayedEasing)));
            Assert.That(items[^1].DisplayName, Is.EqualTo("Custom wave"));
            Assert.That(items[^1].Description, Is.EqualTo("A registered test easing"));
            Assert.That(items.Count(item => item.Type == typeof(LinearEasing)), Is.EqualTo(1));
            Assert.That(items.Select(item => item.Type),
                Has.None.EqualTo(typeof(AbstractEasing))
                    .And.None.EqualTo(typeof(ParameterizedEasing))
                    .And.None.EqualTo(typeof(ThrowingEasing)));
            Assert.That(splines.Select(item => item.Type), Is.EqualTo(new[] { typeof(SplineEasing), typeof(DerivedSpline) }));
        });
    }

    [Test]
    public void Library_registered_names_take_precedence_over_display_attributes()
    {
        var registration = new SingleTypeLibraryItem(
            KnownLibraryItemFormats.Easing, typeof(DisplayedEasing), "Library wave", "Registered description");
        EasingItem item = EasingEditorItems.Create(
                typeof(Easing), [typeof(DisplayedEasing)], type => type == typeof(DisplayedEasing) ? registration : null)
            .Single(item => item.Type == typeof(DisplayedEasing));

        Assert.Multiple(() =>
        {
            Assert.That(item.DisplayName, Is.EqualTo("Library wave"));
            Assert.That(item.Description, Is.EqualTo("Registered description"));
        });
    }

    [AvaloniaTest]
    public void Choosing_an_easing_writes_a_new_instance_as_one_history_entry()
    {
        var holder = new EasingHolder();
        Easing original = holder.Curve.Value;
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 320);
        EasingEditor editor = scope.Editor;
        Window window = scope.Window;
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
                Assert.That(holder.Curve.Value, Is.TypeOf<BackEaseOut>());
                Assert.That(holder.Curve.Value, Is.Not.SameAs(back.Preview),
                    "The item preview must not be shared with the property.");
                Assert.That(scope.History.UndoCount, Is.EqualTo(1));
            });

            Assert.That(scope.History.Undo(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.Value, Is.SameAs(original));
                Assert.That(SelectedType(comboBox), Is.EqualTo(typeof(LinearEasing)));
                Assert.That(scope.History.UndoCount, Is.EqualTo(0), "Following an undo must not record an edit.");
                Assert.That(scope.History.RedoCount, Is.EqualTo(1));
            });

            Assert.That(scope.History.Redo(), Is.True);
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
        holder.Curve.Value = original;
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 400);
        EasingEditor editor = scope.Editor;
        Window window = scope.Window;
        try
        {
            window.Show();
            EasingCurveEditor curve = editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, start);

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.4, 0.3));

            var dragging = (SplineEasing)holder.Curve.Value;
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

            var edited = (SplineEasing)holder.Curve.Value;
            Assert.Multiple(() =>
            {
                Assert.That(edited.X1, Is.EqualTo(0.5).Within(0.01));
                Assert.That(edited.Y1, Is.EqualTo(0.6).Within(0.01));
                Assert.That(edited.X2, Is.EqualTo(0.25f));
                Assert.That(edited.Y2, Is.EqualTo(1f));
                Assert.That(scope.History.UndoCount, Is.EqualTo(1));
            });

            Assert.That(scope.History.Undo(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.Value, Is.SameAs(original));
                Assert.That(curve.Easing, Is.SameAs(original));
                Assert.That(original.X1, Is.EqualTo(0.25f));
            });
            Assert.That(scope.History.Redo(), Is.True);
            Assert.That(holder.Curve.Value, Is.SameAs(edited));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Rejected_drag_steps_keep_showing_the_property_value()
    {
        var holder = new EasingHolder();
        Easing original = holder.Bounded.Value;
        using var scope = new CurveEditorScope(holder, holder.Bounded, height: 400);
        EasingEditor editor = scope.Editor;
        Window window = scope.Window;
        try
        {
            window.Show();
            EasingCurveEditor curve = editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, start);

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            // The validator rejects overshoot, so the property keeps its value and raises no change.
            window.MouseMove(ToWindow(window, curve, 0.25, 1.4));
            Assert.Multiple(() =>
            {
                Assert.That(holder.Bounded.Value, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
                Assert.That(curve.Easing, Is.SameAs(original));
            });

            Point end = ToWindow(window, curve, 0.4, 0.6);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(((SplineEasing)holder.Bounded.Value).Y1, Is.EqualTo(0.6).Within(0.01));
                Assert.That(curve.Easing, Is.SameAs(holder.Bounded.Value));
                Assert.That(scope.History.UndoCount, Is.EqualTo(1));
            });
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
            TimelineTestHost.WaitForHitTest(window, curve, start);

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
    public void A_drag_back_to_the_press_position_restores_the_original_without_an_edit()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        var curve = new EasingCurveEditor { Easing = spline };
        var commits = new List<EasingCurveEditedEventArgs>();
        curve.Editing += (_, e) => curve.Easing = e.NewValue;
        curve.Edited += (_, e) =>
        {
            commits.Add(e);
            curve.Easing = e.NewValue;
        };
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            // Off-centre on the handle: the press alone must not move the control point.
            Point press = ToWindow(window, curve, 0.25, 0.1) + new Vector(3, -2);
            TimelineTestHost.WaitForHitTest(window, curve, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.6, 0.6));
            Assert.That(curve.Easing, Is.Not.SameAs(spline));
            window.MouseMove(press);
            window.MouseUp(press, MouseButton.Left);

            Assert.Multiple(() =>
            {
                Assert.That(commits, Has.Count.EqualTo(1));
                Assert.That(commits[0].OldValue, Is.SameAs(spline));
                Assert.That(commits[0].NewValue, Is.SameAs(spline), "An unchanged drag reports the original as the result.");
                Assert.That(curve.Easing, Is.SameAs(spline));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void An_unchanged_drag_commits_nothing_and_restores_the_original_instance()
    {
        var holder = new EasingHolder();
        var original = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        holder.Curve.Value = original;
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 400);
        EasingEditor editor = scope.Editor;
        Window window = scope.Window;
        try
        {
            window.Show();
            EasingCurveEditor curve = editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            Point press = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.6, 0.6));
            Assert.That(holder.Curve.Value, Is.Not.SameAs(original));
            window.MouseMove(press);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.Value, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
                Assert.That(scope.History.UndoCount, Is.Zero);
                Assert.That(scope.History.HasPendingOperations, Is.False);
            });

            // A commit from another feature, such as the one before an export, must not record the drag.
            scope.History.Commit();
            Assert.That(scope.History.UndoCount, Is.Zero);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void An_unchanged_drag_keeps_another_features_pending_edit()
    {
        var holder = new EasingHolder();
        var original = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        holder.Curve.Value = original;
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 400);
        EasingEditor editor = scope.Editor;
        Window window = scope.Window;
        try
        {
            window.Show();
            EasingCurveEditor curve = editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            Point press = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, press);
            // Another feature's edit that has not been committed yet, like a timeline nudge.
            Easing previous = holder.Spline.Value;
            holder.Spline.Value = new SplineEasing(0.5f, 0.5f, 0.5f, 0.5f);
            Assert.That(scope.History.HasPendingOperations, Is.True);
            Easing nudged = holder.Spline.Value;

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.6, 0.6));
            window.MouseMove(press);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Spline.Value, Is.SameAs(nudged), "The other edit must survive.");
                Assert.That(scope.History.HasPendingOperations, Is.True, "The drag must not commit the other edit early.");
                Assert.That(scope.History.UndoCount, Is.Zero);
                Assert.That(holder.Curve.Value, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
            });

            // The other edit is still in history, not only in the property.
            scope.History.Commit();
            Assert.That(scope.History.Undo(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(holder.Spline.Value, Is.SameAs(previous));
                Assert.That(holder.Curve.Value, Is.SameAs(original));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void A_rejected_easing_choice_keeps_showing_the_property_value()
    {
        var holder = new EasingHolder();
        Easing original = holder.Bounded.Value;
        using var scope = new CurveEditorScope(holder, holder.Bounded, height: 320);
        EasingEditor editor = scope.Editor;
        Window window = scope.Window;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ComboBox comboBox = editor.GetVisualDescendants().OfType<ComboBox>().Single();

            comboBox.SelectedItem = editor.Items!.Single(item => item.Type == typeof(ElasticEaseOut));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Bounded.Value, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
                Assert.That(SelectedType(comboBox), Is.EqualTo(typeof(SplineEasing)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Only_the_pointer_that_began_a_drag_moves_or_ends_it()
    {
        var curve = new EasingCurveEditor { Easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1f) };
        int edits = 0;
        int commits = 0;
        curve.Editing += (_, e) =>
        {
            edits++;
            curve.Easing = e.NewValue;
        };
        curve.Edited += (_, _) => commits++;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Point press = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, press);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);

            var touch = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: false);
            Point elsewhere = ToWindow(window, curve, 0.8, 0.8);
            curve.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, curve, touch, window, elsewhere,
                0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
            curve.RaiseEvent(new PointerReleasedEventArgs(curve, touch, window, elsewhere,
                0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, MouseButton.None));
            // A real touch is implicitly captured, so its release also raises a capture loss.
            curve.RaiseEvent(new PointerCaptureLostEventArgs(curve, touch));
            Assert.Multiple(() =>
            {
                Assert.That(edits, Is.Zero);
                Assert.That(commits, Is.Zero);
                Assert.That(curve.IsDragging, Is.True);
            });

            Point end = ToWindow(window, curve, 0.5, 0.5);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            Assert.That((edits, commits), Is.EqualTo((1, 1)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void A_non_finite_control_point_leaves_the_other_handle_editable(bool nanFirst)
    {
        var curve = new EasingCurveEditor
        {
            Easing = nanFirst ? new SplineEasing(0.25f, float.NaN, 0.75f, 1f) : new SplineEasing(0.25f, 0.1f, 0.75f, float.NaN)
        };
        int edits = 0;
        curve.Editing += (_, _) => edits++;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            // Placed in the [0, 1] range, which the NaN handle must not turn into NaN.
            Point finite = nanFirst ? ToWindow(window, curve, 0.75, 1) : ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, finite);

            // Far from the drawable handle: the NaN handle must not be grabbed instead.
            Drag(window, ToWindow(window, curve, 0.5, 0.5), ToWindow(window, curve, 0.6, 0.4));
            Assert.That(edits, Is.Zero);

            Drag(window, finite, ToWindow(window, curve, 0.4, 0.8));
            Assert.That(edits, Is.GreaterThan(0));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void An_unchanged_drag_beside_a_non_finite_control_point_reports_the_original()
    {
        var spline = new SplineEasing(0.25f, float.NaN, 0.25f, 1f);
        var curve = new EasingCurveEditor { Easing = spline };
        var commits = new List<EasingCurveEditedEventArgs>();
        curve.Editing += (_, e) => curve.Easing = e.NewValue;
        curve.Edited += (_, e) => commits.Add(e);
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Point press = ToWindow(window, curve, 0.25, 1);
            TimelineTestHost.WaitForHitTest(window, curve, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.6, 0.6));
            window.MouseMove(press);
            window.MouseUp(press, MouseButton.Left);

            Assert.That(commits, Has.Count.EqualTo(1));
            Assert.That(commits[0].NewValue, Is.SameAs(spline));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_arrow_keys_move_the_selected_control_point_as_one_history_entry_each()
    {
        var holder = new EasingHolder();
        var original = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        holder.Curve.Value = original;
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 400);
        Window window = scope.Window;
        try
        {
            window.Show();
            EasingCurveEditor curve = scope.Editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            HeadlessTestHelpers.Render();

            Assert.That(curve.Focus(NavigationMethod.Tab), Is.True);
            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
            HeadlessTestHelpers.Settle();

            var edited = (SplineEasing)holder.Curve.Value;
            Assert.Multiple(() =>
            {
                Assert.That(edited.X1, Is.EqualTo(0.26f).Within(0.0001f));
                Assert.That(edited.Y1, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(edited.X2, Is.EqualTo(0.25f));
                Assert.That(edited.Y2, Is.EqualTo(1f));
                Assert.That(original.X1, Is.EqualTo(0.25f), "The displayed spline must not be mutated.");
                Assert.That(scope.History.UndoCount, Is.EqualTo(2));
            });

            Assert.That(scope.History.Undo(), Is.True);
            Assert.That(scope.History.Undo(), Is.True);
            HeadlessTestHelpers.Settle();
            Assert.That(holder.Curve.Value, Is.SameAs(original));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Tab_reaches_the_second_control_point_and_then_leaves_the_curve()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var curve = new EasingCurveEditor { Easing = spline };
        var after = new Button { Content = "after" };
        var window = new Window { Content = new StackPanel { Children = { curve, after } }, Width = 300, Height = 300 };
        var edits = new List<SplineEasing>();
        curve.Edited += (_, e) => edits.Add((SplineEasing)e.NewValue!);
        curve.Editing += (_, e) => curve.Easing = e.NewValue;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(curve.Focus(NavigationMethod.Tab), Is.True);

            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.That(curve.IsFocused, Is.True);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(EasingEditorTests), "keyboard-focus");
            Press(window, Key.Left, PhysicalKey.ArrowLeft);
            Assert.That(edits.Single().X2, Is.EqualTo(0.74f).Within(0.0001f));
            Assert.That(edits.Single().X1, Is.EqualTo(0.25f));

            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.That(after.IsFocused, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    // A value column alone leaves too little plot to place a handle with any precision.
    [AvaloniaTest]
    [TestCase(PropertyEditorStyle.Normal)]
    [TestCase(PropertyEditorStyle.Compact)]
    public void The_curve_takes_the_whole_width_of_its_row(PropertyEditorStyle style)
    {
        var holder = new EasingHolder();
        holder.Curve.Value = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 400);
        scope.Editor.EditorStyle = style;
        try
        {
            scope.Window.Show();
            HeadlessTestHelpers.Render();

            EasingCurveEditor curve = scope.Editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            Assert.That(curve.Bounds.Width, Is.GreaterThanOrEqualTo(scope.Editor.Bounds.Width - 8));
        }
        finally
        {
            scope.Window.Close();
        }
    }

    // A wide row's value column has room for the curve, which then lines up with the box that chooses the easing.
    [AvaloniaTest]
    public void The_curve_sits_under_its_box_in_a_wide_row()
    {
        var holder = new EasingHolder();
        holder.Curve.Value = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        using var scope = new CurveEditorScope(holder, holder.Curve, height: 400);
        scope.Window.Width = 700;
        try
        {
            scope.Window.Show();
            HeadlessTestHelpers.Render();

            EasingCurveEditor curve = scope.Editor.GetVisualDescendants().OfType<EasingCurveEditor>().Single();
            ComboBox box = scope.Editor.GetVisualDescendants().OfType<ComboBox>().Single();
            double Left(Control control) => control.TranslatePoint(default, scope.Window)!.Value.X;
            Assert.Multiple(() =>
            {
                Assert.That(Left(curve), Is.EqualTo(Left(box)).Within(0.5));
                Assert.That(Left(curve) + curve.Bounds.Width, Is.EqualTo(Left(box) + box.Bounds.Width).Within(0.5));
            });
        }
        finally
        {
            scope.Window.Close();
        }
    }

    // A handle at the end of the range sits on the corner of the plot, where its ring must neither be cut off
    // nor run under the focus border, which takes the outer 3 pixels when the curve is reached with Tab.
    [AvaloniaTest]
    public void The_focus_ring_stays_whole_on_the_corners_of_the_plot()
    {
        var curve = new EasingCurveEditor
        {
            Easing = new SplineEasing(0, -0.5f, 1, 1.5f),
            Background = Brushes.Black,
            GridBrush = Brushes.Black,
            CurveBrush = Brushes.Black,
            HandleBrush = Brushes.White,
            FocusAdorner = null
        };
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.That(curve.Focus(NavigationMethod.Tab), Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(PixelsDrawnNearTheEdge(window, curve), Is.Empty);

            Press(window, Key.Tab, PhysicalKey.Tab);
            HeadlessTestHelpers.Render();
            Assert.That(PixelsDrawnNearTheEdge(window, curve), Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    private static PixelPoint[] PixelsDrawnNearTheEdge(Window window, EasingCurveEditor curve)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        int width = (int)curve.Bounds.Width;
        int height = (int)curve.Bounds.Height;
        return
        [
            .. from y in Enumerable.Range(0, height)
               from x in Enumerable.Range(0, width)
               where Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y)) < 3
               let point = new PixelPoint(x, y)
               where HeadlessTestHelpers.ReadPixel(frame, point) != (0, 0, 0)
               select point
        ];
    }

    [AvaloniaTest]
    public void The_arrow_keys_keep_X_inside_the_curve_and_let_Y_overshoot()
    {
        var curve = new EasingCurveEditor { Easing = new SplineEasing(0.05f, 0.95f, 0.75f, 0.9f) };
        curve.Editing += (_, e) => curve.Easing = e.NewValue;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(curve.Focus(NavigationMethod.Tab), Is.True);

            Press(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
            Press(window, Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Shift);

            var spline = (SplineEasing)curve.Easing!;
            Assert.Multiple(() =>
            {
                Assert.That(spline.X1, Is.Zero);
                Assert.That(spline.Y1, Is.EqualTo(1.05f).Within(0.0001f));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Only_a_curve_with_handles_to_move_takes_focus()
    {
        var curve = new EasingCurveEditor();
        Assert.That(curve.Focusable, Is.False);

        curve.Easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        Assert.That(curve.Focusable, Is.True);

        curve.IsReadOnly = true;
        Assert.That(curve.Focusable, Is.False);
    }

    [AvaloniaTest]
    public void Assistive_technology_hears_the_values_of_the_control_points()
    {
        var curve = new EasingCurveEditor { Easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f) };
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(curve);

            Assert.Multiple(() =>
            {
                Assert.That(peer.GetName(), Is.EqualTo(Strings.Easing));
                Assert.That(peer.GetHelpText(), Does.Contain(0.25f.ToString(CultureInfo.CurrentCulture)));
                Assert.That(peer.GetHelpText(), Does.Contain(0.9f.ToString(CultureInfo.CurrentCulture)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Assistive_technology_hears_which_control_point_the_arrow_keys_move()
    {
        var curve = new EasingCurveEditor { Easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f) };
        curve.Editing += (_, e) => curve.Easing = e.NewValue;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(curve);
            Assert.That(curve.Focus(NavigationMethod.Tab), Is.True);
            Assert.That(peer.GetHelpText(), Does.StartWith(Strings.EasingCurve_FirstSelected));

            var announced = new List<string?>();
            peer.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.HelpTextProperty)
                    announced.Add(e.NewValue as string);
            };

            Press(window, Key.Tab, PhysicalKey.Tab);

            Assert.That(peer.GetHelpText(), Does.StartWith(Strings.EasingCurve_SecondSelected));
            Assert.That(announced, Is.EqualTo(new[] { peer.GetHelpText() }));

            Press(window, Key.Up, PhysicalKey.ArrowUp);

            Assert.That(announced, Has.Count.EqualTo(2), "the values changed");
            Assert.That(announced[1], Is.EqualTo(peer.GetHelpText()));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Assistive_technology_hears_changes_that_do_not_come_from_the_keyboard()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f);
        var curve = new EasingCurveEditor { Easing = spline };
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(curve);
            var announced = new List<string?>();
            peer.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.HelpTextProperty)
                    announced.Add(e.NewValue as string);
            };

            spline.X1 = 0.5f;
            Assert.That(announced, Has.Count.EqualTo(1), "the spline changed in place");
            Assert.That(announced[^1], Is.EqualTo(peer.GetHelpText()));

            curve.Easing = spline = new SplineEasing(0.3f, 0.2f, 0.6f, 0.8f);
            Assert.That(announced, Has.Count.EqualTo(2), "another spline took its place");
            Assert.That(announced[^1], Is.EqualTo(peer.GetHelpText()));

            Point handle = ToWindow(window, curve, 0.6, 0.8);
            TimelineTestHost.WaitForHitTest(window, curve, handle);
            window.MouseMove(handle);
            window.MouseDown(handle, MouseButton.Left);
            window.MouseUp(handle, MouseButton.Left);
            // The press focuses the curve first, which selects the first handle on the way.
            Assert.That(announced[^1], Does.StartWith(Strings.EasingCurve_SecondSelected), "a press selected a handle");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
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
            TimelineTestHost.WaitForHitTest(window, curve, start);

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

    [AvaloniaTest]
    public void Becoming_read_only_mid_drag_confirms_the_drag_and_releases_the_pointer()
    {
        var spline = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        var curve = new EasingCurveEditor { Easing = spline };
        var commits = new List<EasingCurveEditedEventArgs>();
        int captureLost = 0;
        curve.Editing += (_, e) => curve.Easing = e.NewValue;
        curve.Edited += (_, e) => commits.Add(e);
        curve.PointerCaptureLost += (_, _) => captureLost++;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, start);

            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            Point moved = ToWindow(window, curve, 0.5, 0.5);
            window.MouseMove(moved);
            curve.IsReadOnly = true;

            Assert.Multiple(() =>
            {
                Assert.That(curve.IsDragging, Is.False);
                Assert.That(captureLost, Is.EqualTo(1), "Ending the drag must release the pointer before the button is.");
                Assert.That(commits, Has.Count.EqualTo(1));
                Assert.That(commits[0].OldValue, Is.SameAs(spline));
            });

            window.MouseUp(moved, MouseButton.Left);
            Assert.That(commits, Has.Count.EqualTo(1));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Spline_subclasses_are_drawn_but_not_dragged()
    {
        var curve = new EasingCurveEditor { Easing = new DerivedSpline() };
        int events = 0;
        curve.Editing += (_, _) => events++;
        curve.Edited += (_, _) => events++;
        var window = new Window { Content = curve, Width = 300, Height = 200 };
        try
        {
            window.Show();
            Point start = ToWindow(window, curve, 0.25, 0.1);
            TimelineTestHost.WaitForHitTest(window, curve, start);

            Drag(window, start, ToWindow(window, curve, 0.6, 0.6));
            Assert.That(events, Is.Zero, "A drag step would turn the subclass into a plain SplineEasing.");

            // A plain spline with the same control points is dragged by the same gesture.
            curve.Easing = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
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

    // An EasingEditor for the property in a window that is not shown yet, with History recording its edits.
    private sealed class CurveEditorScope : IDisposable
    {
        private readonly PropertyEditorViewModel _vm;
        private readonly HistoryScope _history;

        public CurveEditorScope(EasingHolder holder, IProperty<Easing> property, double height)
        {
            (_vm, Control control) = CreateControl(new EnginePropertyAdapter<Easing>(property, holder));
            _history = new HistoryScope(holder);
            _vm.Accept(ServiceVisitor.For(_history.History));
            Editor = (EasingEditor)control;
            Window = new Window { Content = Editor, Width = 480, Height = height };
        }

        public EasingEditor Editor { get; }

        public Window Window { get; }

        public HistoryManager History => _history.History;

        public void Dispose()
        {
            _history.Dispose();
            _vm.Dispose();
        }
    }

    [SuppressResourceClassGeneration]
    public sealed class EasingHolder : EngineObject
    {
        public EasingHolder() => ScanProperties<EasingHolder>();

        public IExpressionProperty<Easing> Curve { get; } = Property.CreateExpression<Easing>(new LinearEasing());

        public IExpressionProperty<SplineEasing> Spline { get; } = Property.CreateExpression(new SplineEasing());

        // ScanProperties builds the validator from the property's attributes.
        [NoOvershoot]
        public IExpressionProperty<Easing> Bounded { get; } = Property.CreateExpression<Easing>(new SplineEasing(0.25f, 0.1f, 0.25f, 1f));
    }

    public sealed class NoOvershootAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
            => value is not (SplineEasing { Y1: > 1 } or SplineEasing { Y2: > 1 } or ElasticEaseOut);
    }

    [Display(Name = "Custom wave", Description = "A registered test easing")]
    public sealed class DisplayedEasing : Easing
    {
        public override float Ease(float progress) => progress;
    }

    public abstract class AbstractEasing : Easing;

    public sealed class ParameterizedEasing(float scale) : Easing
    {
        public override float Ease(float progress) => progress * scale;
    }

    public sealed class DerivedSpline() : SplineEasing(0.25f, 0.1f, 0.25f, 1f);

    public sealed class ThrowingEasing : Easing
    {
        public ThrowingEasing() => throw new InvalidOperationException("The extension is not ready.");

        public override float Ease(float progress) => progress;
    }
}
