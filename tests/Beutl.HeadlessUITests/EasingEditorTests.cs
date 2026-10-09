using System.ComponentModel.DataAnnotations;
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
using Beutl.Services;
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
    public void Rejected_drag_steps_keep_showing_the_property_value()
    {
        var holder = new EasingHolder();
        Easing original = holder.Bounded.CurrentValue;
        var (vm, control) = CreateControl(new EnginePropertyAdapter<Easing>(holder.Bounded, holder));
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
            // The validator rejects overshoot, so the property keeps its value and raises no change.
            window.MouseMove(ToWindow(window, curve, 0.25, 1.4));
            Assert.Multiple(() =>
            {
                Assert.That(holder.Bounded.CurrentValue, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
                Assert.That(curve.Easing, Is.SameAs(original));
            });

            Point end = ToWindow(window, curve, 0.4, 0.6);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(((SplineEasing)holder.Bounded.CurrentValue).Y1, Is.EqualTo(0.6).Within(0.01));
                Assert.That(curve.Easing, Is.SameAs(holder.Bounded.CurrentValue));
                Assert.That(history.History.UndoCount, Is.EqualTo(1));
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
            WaitForHitTest(window, curve, press);

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
            Point press = ToWindow(window, curve, 0.25, 0.1);
            WaitForHitTest(window, curve, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.6, 0.6));
            Assert.That(holder.Curve.CurrentValue, Is.Not.SameAs(original));
            window.MouseMove(press);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Curve.CurrentValue, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
                Assert.That(history.History.UndoCount, Is.Zero);
            });
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
            Point press = ToWindow(window, curve, 0.25, 0.1);
            WaitForHitTest(window, curve, press);
            // Another feature's edit that has not been committed yet, like a timeline nudge.
            Easing previous = holder.Spline.CurrentValue;
            holder.Spline.CurrentValue = new SplineEasing(0.5f, 0.5f, 0.5f, 0.5f);
            Assert.That(history.History.HasPendingOperations, Is.True);
            Easing nudged = holder.Spline.CurrentValue;

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(ToWindow(window, curve, 0.6, 0.6));
            window.MouseMove(press);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Spline.CurrentValue, Is.SameAs(nudged), "The other edit must survive.");
                Assert.That(history.History.HasPendingOperations, Is.True, "The drag must not commit the other edit early.");
                Assert.That(history.History.UndoCount, Is.Zero);
                Assert.That(holder.Curve.CurrentValue, Is.SameAs(original));
                Assert.That(editor.Value, Is.SameAs(original));
            });

            // The other edit is still in history, not only in the property.
            history.History.Commit();
            Assert.That(history.History.Undo(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(holder.Spline.CurrentValue, Is.SameAs(previous));
                Assert.That(holder.Curve.CurrentValue, Is.SameAs(original));
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
        Easing original = holder.Bounded.CurrentValue;
        var (vm, control) = CreateControl(new EnginePropertyAdapter<Easing>(holder.Bounded, holder));
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

            comboBox.SelectedItem = editor.Items!.Single(item => item.Type == typeof(ElasticEaseOut));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(holder.Bounded.CurrentValue, Is.SameAs(original));
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
            WaitForHitTest(window, curve, press);
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
            WaitForHitTest(window, curve, finite);

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
            WaitForHitTest(window, curve, press);

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
            WaitForHitTest(window, curve, start);

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
            WaitForHitTest(window, curve, start);

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

        // ScanProperties builds the validator from the property's attributes.
        [NoOvershoot]
        public IProperty<Easing> Bounded { get; } = Property.Create<Easing>(new SplineEasing(0.25f, 0.1f, 0.25f, 1f));
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
