using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PropertyEditorGridTests
{
    [AvaloniaTest]
    [TestCase(760, 0, 120, 1d)]
    [TestCase(760, 3, 120, 1d)]
    [TestCase(1040, 0, 120, 1d)]
    [TestCase(1040, 3, 120, 1d)]
    [TestCase(760, 3, 180, 1.25d)]
    [TestCase(1040, 3, 180, 1.25d)]
    public void Rightmost_shared_splitter_preserves_nested_values_and_trailing_controls(int width, int keyFrames, int minimum, double scale)
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float> { Header = "Opacity", Value = 75 };
        var color = new ColorEditor { Header = "Color" };
        var alignment = new AlignmentXEditor { Header = "Alignment X" };
        var vector = new Vector2Editor<float> { Header = "Scale", FirstValue = 120, SecondValue = 80 };
        PropertyEditor[] nestedEditors = [nested, color, alignment, vector];
        foreach (var editor in nestedEditors)
        {
            editor.KeyFrameCount = keyFrames;
            editor.MenuContent = new Border { Width = 24, Height = 24 };
        }
        outer.MenuContent = new Border { Width = 24, Height = 24 };
        var rows = new StackPanel { Margin = new Thickness(0, 0, 48, 0) };
        foreach (var editor in nestedEditors) rows.Children.Add(editor);
        var scope = new StackPanel
        {
            Children = { outer, new TreeLineDecorator { Child = new TreeLineDecorator { Child = rows } } }
        };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = width, Height = 600 };
        try
        {
            window.Show();
            window.SetRenderScaling(scale);
            HeadlessTestHelpers.Render(3);
            GetBox(nested).MinWidth = minimum;
            HeadlessTestHelpers.Render(3);
            MoveSplitterToMaximum(window, outer);
            AssertValuesFit(scope, [outer, .. nestedEditors]);
            double rightmost = GetBox(outer).TranslatePoint(default, scope)!.Value.X;
            Assert.That(rightmost, Is.GreaterThan(width / 2));

            MoveSplitterToMaximum(window, outer);
            Assert.That(GetBox(outer).TranslatePoint(default, scope)!.Value.X, Is.EqualTo(rightmost).Within(1));
            AssertValuesFit(scope, [outer, .. nestedEditors]);

            var splitter = outer.GetVisualDescendants().OfType<GridSplitter>().Single();
            splitter.KeyboardIncrement = 30;
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            HeadlessTestHelpers.Render(3);
            Assert.That(GetBox(outer).TranslatePoint(default, scope)!.Value.X, Is.LessThan(rightmost));
            AssertValuesFit(scope, [outer, .. nestedEditors]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Right_edge_is_reclamped_when_a_nested_row_appears_or_the_scope_shrinks(bool reveal)
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float>
        {
            Header = "Opacity",
            Value = 75,
            KeyFrameCount = 3,
            MenuContent = new Border { Width = 24, Height = 24 }
        };
        var branch = new TreeLineDecorator
        {
            IsVisible = !reveal,
            Child = new StackPanel { Margin = new Thickness(0, 0, 48, 0), Children = { nested } }
        };
        var scope = new StackPanel { Children = { outer, branch } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = 1040, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            MoveSplitterToMaximum(window, outer);
            double ratio = PropertyEditorGrid.GetValueColumnRatio(scope);
            if (reveal) branch.IsVisible = true;
            else window.Width = 640;
            HeadlessTestHelpers.Render(3);
            AssertValuesFit(scope, [outer, nested]);
            Assert.That(PropertyEditorGrid.GetValueColumnRatio(scope), Is.LessThan(ratio));
        }
        finally { window.Close(); }
    }

    private static void MoveSplitterToMaximum(Window window, PropertyEditor editor)
    {
        var splitter = editor.GetVisualDescendants().OfType<GridSplitter>().Single();
        Assert.That(splitter.Focus(), Is.True);
        splitter.KeyboardIncrement = window.Width;
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        HeadlessTestHelpers.Render(3);
    }

    private static void AssertValuesFit(Control scope, PropertyEditor[] editors)
    {
        double left = GetBox(editors[0]).TranslatePoint(default, scope)!.Value.X;
        foreach (var editor in editors)
        {
            PropertyEditorGrid grid = GetGrid(editor);
            Assert.That(grid.ColumnDefinitions[0].Width.IsAbsolute, Is.True, editor.Header);
            Assert.That(GetBox(editor).TranslatePoint(default, scope)!.Value.X, Is.EqualTo(left).Within(1), editor.Header);
            foreach (Control child in grid.Children.Where(c => c.IsVisible && Grid.GetRow(c) == 0 && Grid.GetColumn(c) >= grid.ValueColumn))
            {
                Assert.That(child.Bounds.Width, Is.GreaterThanOrEqualTo(child.MinWidth - 1), editor.Header);
                Assert.That(child.Bounds.Right + child.Margin.Right, Is.LessThanOrEqualTo(grid.Bounds.Width + 1), editor.Header);
            }
            foreach (Control input in editor.GetVisualDescendants().OfType<Control>()
                         .Where(c => c.IsEffectivelyVisible && c is TextBox or Button))
            {
                double right = input.TranslatePoint(default, grid)!.Value.X + input.Bounds.Width;
                Assert.That(right, Is.LessThanOrEqualTo(grid.Bounds.Width + 1), editor.Header);
            }
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Revealing_a_deeper_row_reclamps_the_shared_splitter(bool addAfterLayout)
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float> { Header = "Opacity", Value = 75, KeyFrameCount = 3 };
        var branch = new TreeLineDecorator
        {
            IsVisible = false,
            Child = new TreeLineDecorator
            {
                Child = new TreeLineDecorator
                {
                    Child = new StackPanel { Margin = new Thickness(0, 0, 7, 0), Children = { nested } }
                }
            }
        };
        var scope = new StackPanel { Children = { outer } };
        if (!addAfterLayout) scope.Children.Add(branch);
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = 760, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            MoveSplitterToMinimum(window, outer);
            double before = GetBox(outer).TranslatePoint(default, scope)!.Value.X;

            if (addAfterLayout) scope.Children.Add(branch);
            branch.IsVisible = true;
            HeadlessTestHelpers.Render(3);
            AssertMinimumAlignment(scope, outer, nested);
            Assert.That(GetBox(outer).TranslatePoint(default, scope)!.Value.X, Is.GreaterThan(before));
            double ratio = PropertyEditorGrid.GetValueColumnRatio(scope);
            HeadlessTestHelpers.Render(3);
            Assert.That(PropertyEditorGrid.GetValueColumnRatio(scope), Is.EqualTo(ratio));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(640, 1d)]
    [TestCase(640, 1.25d)]
    [TestCase(760, 1d)]
    [TestCase(760, 1.25d)]
    public void Shrinking_a_wide_scope_reclamps_without_overadjusting_the_shared_splitter(int width, double scale)
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float> { Header = "Opacity", Value = 75 };
        var scope = new StackPanel
        {
            Children =
            {
                outer,
                new TreeLineDecorator
                {
                    Child = new TreeLineDecorator
                    {
                        Child = new StackPanel { Margin = new Thickness(0, 0, 7, 0), Children = { nested } }
                    }
                }
            }
        };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = 1040, Height = 300 };
        try
        {
            window.Show();
            window.SetRenderScaling(scale);
            HeadlessTestHelpers.Render(3);
            MoveSplitterToMinimum(window, outer);
            double before = PropertyEditorGrid.GetValueColumnRatio(scope);

            window.Width = width;
            HeadlessTestHelpers.Render(3);
            AssertMinimumAlignment(scope, outer, nested);
            double ratio = PropertyEditorGrid.GetValueColumnRatio(scope);
            Assert.That(ratio, Is.GreaterThan(before));

            window.Width = 1040;
            HeadlessTestHelpers.Render(3);
            Assert.That(PropertyEditorGrid.GetValueColumnRatio(scope), Is.EqualTo(ratio));
            foreach (var editor in new[] { outer, nested })
                Assert.That(GetBox(editor).TranslatePoint(default, scope)!.Value.X,
                    Is.EqualTo(scope.Bounds.Width * ratio).Within(1));
        }
        finally { window.Close(); }
    }

    private static void MoveSplitterToMinimum(Window window, PropertyEditor editor)
    {
        var splitter = editor.GetVisualDescendants().OfType<GridSplitter>().Single();
        Assert.That(splitter.Focus(), Is.True);
        splitter.KeyboardIncrement = window.Width;
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        window.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        HeadlessTestHelpers.Render(3);
    }

    private static void AssertMinimumAlignment(Control scope, PropertyEditor outer, PropertyEditor nested)
    {
        foreach (var editor in new[] { outer, nested })
        {
            Assert.That(GetGrid(editor).ColumnDefinitions[0].Width.IsAbsolute, Is.True);
            Assert.That(GetBox(editor).TranslatePoint(default, scope)!.Value.X,
                Is.EqualTo(GetBox(outer).TranslatePoint(default, scope)!.Value.X).Within(1));
        }
        Assert.That(GetGrid(nested).ColumnDefinitions[0].ActualWidth, Is.EqualTo(80).Within(1),
            "Only increase the shared position enough to preserve the deepest label's minimum width.");
    }

    [AvaloniaTest]
    [TestCase(760, false)]
    [TestCase(760, true)]
    [TestCase(1040, false)]
    [TestCase(1040, true)]
    public void Splitter_keeps_rows_aligned_at_the_minimum_label_width_and_can_move_back(int width, bool moveNested)
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float> { Header = "Opacity", Value = 75, KeyFrameCount = 3 };
        var scope = new StackPanel
        {
            Children = { outer, new TreeLineDecorator { Child = new TreeLineDecorator { Child = nested } } }
        };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = width, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var splitter = (moveNested ? nested : outer).GetVisualDescendants().OfType<GridSplitter>().Single();
            Assert.That(splitter.Focus(), Is.True);
            splitter.KeyboardIncrement = width;
            Move(Key.Left, PhysicalKey.ArrowLeft);
            double minimum = GetBox(outer).TranslatePoint(default, scope)!.Value.X;
            Assert.That(minimum, Is.LessThan(width / 2));
            AssertAligned();

            // Repeated movement against the limit must not disconnect any row.
            Move(Key.Left, PhysicalKey.ArrowLeft);
            Assert.That(GetBox(outer).TranslatePoint(default, scope)!.Value.X, Is.EqualTo(minimum).Within(1));
            AssertAligned();

            splitter.KeyboardIncrement = 30;
            Move(Key.Right, PhysicalKey.ArrowRight);
            Assert.That(GetBox(outer).TranslatePoint(default, scope)!.Value.X, Is.GreaterThan(minimum));
            AssertAligned();
        }
        finally { window.Close(); }

        void Move(Key key, PhysicalKey physicalKey)
        {
            window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
            window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
            HeadlessTestHelpers.Render(3);
        }

        void AssertAligned()
        {
            foreach (var editor in new[] { outer, nested })
            {
                Assert.That(GetGrid(editor).ColumnDefinitions[0].Width.IsAbsolute, Is.True);
                Assert.That(GetGrid(editor).ColumnDefinitions[0].ActualWidth, Is.GreaterThanOrEqualTo(80));
                Assert.That(GetBox(editor).TranslatePoint(default, scope)!.Value.X,
                    Is.EqualTo(GetBox(outer).TranslatePoint(default, scope)!.Value.X).Within(1));
            }
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Attached_scope_can_be_enabled_disabled_and_reenabled(bool initiallyEnabled)
    {
        var number = new NumberEditor<float> { Header = "Width", Value = 640 };
        var alignment = new AlignmentXEditor { Header = "Alignment X" };
        var scope = new StackPanel { Children = { number, alignment } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, initiallyEnabled);
        var window = new Window { Content = scope, Width = 760, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            if (!initiallyEnabled) AssertRestored();
            PropertyEditorGrid.SetIsAlignmentScope(scope, true);
            HeadlessTestHelpers.Render(3);
            AssertAligned();

            var input = number.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("720");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                PropertyEditorGrid.SetIsAlignmentScope(scope, false);
                HeadlessTestHelpers.Render(3);
                AssertRestored();

                // A disabled scope must no longer drive its previously aligned descendants.
                PropertyEditorGrid.SetValueColumnRatio(scope, .6 + cycle * .1);
                window.Width = 800 + cycle * 100;
                HeadlessTestHelpers.Render(3);
                AssertRestored();

                PropertyEditorGrid.SetIsAlignmentScope(scope, true);
                HeadlessTestHelpers.Render(3);
                AssertAligned();
                PropertyEditorGrid.SetValueColumnRatio(scope, .5);
                HeadlessTestHelpers.Render(3);
                AssertAligned();
                Assert.That(number.GetVisualDescendants().OfType<TextBox>().Single(), Is.SameAs(input));
                Assert.That(input.Text, Is.EqualTo("720"));
                Assert.That(input.IsFocused, Is.True);
            }
        }
        finally { window.Close(); }

        void AssertRestored()
        {
            Assert.That(GetGrid(number).ColumnDefinitions[0].Width.IsStar, Is.True);
            Assert.That(GetGrid(alignment).ColumnDefinitions[0].Width.IsStar, Is.True);
            Assert.That(GetBox(alignment).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
        }

        void AssertAligned()
        {
            foreach (PropertyEditor editor in new PropertyEditor[] { number, alignment })
            {
                Assert.That(GetGrid(editor).ColumnDefinitions[0].Width.IsAbsolute, Is.True);
                Assert.That(GetBox(editor).TranslatePoint(default, scope)!.Value.X,
                    Is.EqualTo(scope.Bounds.Width * PropertyEditorGrid.GetValueColumnRatio(scope)).Within(1));
            }
            Assert.That(GetBox(alignment).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Left));
        }
    }

    [AvaloniaTest]
    public void Attached_editors_follow_the_nearest_enabled_scope_when_scopes_change()
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float> { Header = "Opacity", Value = 75 };
        var innerScope = new StackPanel { Margin = new Thickness(40, 0, 20, 0), Children = { nested } };
        var outerScope = new StackPanel { Children = { outer, innerScope } };
        PropertyEditorGrid.SetIsAlignmentScope(outerScope, true);
        PropertyEditorGrid.SetValueColumnRatio(innerScope, .65);
        var window = new Window { Content = outerScope, Width = 900, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            AssertInput(outer, 450);
            AssertInput(nested, 450);

            PropertyEditorGrid.SetIsAlignmentScope(innerScope, true);
            HeadlessTestHelpers.Render(3);
            double innerInput = innerScope.Bounds.X + innerScope.Bounds.Width * .65;
            AssertInput(outer, 450);
            AssertInput(nested, innerInput);

            PropertyEditorGrid.SetValueColumnRatio(outerScope, .55);
            HeadlessTestHelpers.Render(3);
            AssertInput(outer, 495);
            AssertInput(nested, innerInput);

            PropertyEditorGrid.SetIsAlignmentScope(innerScope, false);
            HeadlessTestHelpers.Render(3);
            AssertInput(nested, 495);
            PropertyEditorGrid.SetIsAlignmentScope(innerScope, true);
            PropertyEditorGrid.SetIsAlignmentScope(outerScope, false);
            HeadlessTestHelpers.Render(3);
            Assert.That(GetGrid(outer).ColumnDefinitions[0].Width.IsStar, Is.True);
            AssertInput(nested, innerInput);
        }
        finally { window.Close(); }

        void AssertInput(PropertyEditor editor, double expected)
        {
            Assert.That(GetGrid(editor).ColumnDefinitions[0].Width.IsAbsolute, Is.True);
            Assert.That(GetBox(editor).TranslatePoint(default, outerScope)!.Value.X, Is.EqualTo(expected).Within(1));
        }
    }

    [AvaloniaTest]
    public void Nested_inputs_follow_the_shared_splitter_and_keep_edits_during_resize()
    {
        var outer = new NumberEditor<float> { Header = "Width", Value = 640 };
        var nested = new NumberEditor<float> { Header = "Opacity", Value = 75 };
        var color = new ColorEditor { Header = "Color", KeyFrameCount = 3, KeyFrameIndex = 1 };
        var alignment = new AlignmentXEditor { Header = "Alignment X", KeyFrameCount = 3 };
        var vector = new Vector4Editor<float>
        {
            Header = "Rectangle",
            FirstValue = 10,
            SecondValue = 20,
            ThirdValue = 30,
            FourthValue = 40
        };
        var nestedRows = new StackPanel
        {
            Margin = new Thickness(0, 0, 7, 0),
            Children = { nested, color, vector, alignment }
        };
        var scope = new StackPanel
        {
            Children = { outer, new TreeLineDecorator { Child = new TreeLineDecorator { Child = nestedRows } } }
        };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        PropertyEditor[] editors = [outer, nested, color, vector, alignment];
        var window = new Window { Content = scope, Width = 760, Height = 500 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            AssertAligned(380);

            var vectorGrid = GetGrid(vector);
            var header = vectorGrid.Children.OfType<TextBlock>().Single();
            Assert.That(header.TranslatePoint(default, scope)!.Value.Y,
                Is.EqualTo(GetBox(vector).TranslatePoint(default, scope)!.Value.Y).Within(1));

            var input = nested.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("62");
            window.Width = 320;
            HeadlessTestHelpers.Render(3);
            Assert.That(Grid.GetRow(GetGrid(vector).Children.OfType<DataValidationErrors>().Single()), Is.EqualTo(1));
            Assert.That(GetGrid(outer).ColumnDefinitions[0].Width.IsStar, Is.True);
            Assert.That(GetBox(alignment).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
            window.Width = 760;
            HeadlessTestHelpers.Render(3);
            Assert.That(nested.GetVisualDescendants().OfType<TextBox>().Single(), Is.SameAs(input));
            Assert.That(input.Text, Is.EqualTo("62"));
            Assert.That(input.IsFocused, Is.True);
            AssertAligned(380);

            var splitter = outer.GetVisualDescendants().OfType<GridSplitter>().Single();
            Assert.That(splitter.Focus(), Is.True);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            HeadlessTestHelpers.Render(3);
            double moved = GetBox(outer).TranslatePoint(default, scope)!.Value.X;
            Assert.That(moved, Is.GreaterThan(380));
            AssertAligned(moved);

            // Moving the same editors outside the inspector releases the scope and
            // restores their ordinary proportional columns.
            window.Content = null;
            HeadlessTestHelpers.Render();
            PropertyEditorGrid.SetIsAlignmentScope(scope, false);
            window.Content = scope;
            HeadlessTestHelpers.Render(3);
            Assert.That(GetGrid(outer).ColumnDefinitions[0].Width.IsStar, Is.True);
            Assert.That(GetBox(alignment).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
            Assert.That(GetBox(outer).TranslatePoint(default, scope)!.Value.X,
                Is.Not.EqualTo(moved).Within(1));
        }
        finally { window.Close(); }

        void AssertAligned(double expected)
        {
            foreach (PropertyEditor editor in editors)
                Assert.That(GetBox(editor).TranslatePoint(default, scope)!.Value.X,
                    Is.EqualTo(expected).Within(1), editor.Header);
        }
    }

    private static PropertyEditorGrid GetGrid(PropertyEditor editor)
        => editor.GetVisualDescendants().OfType<PropertyEditorGrid>().Single();

    private static Control GetBox(PropertyEditor editor)
    {
        PropertyEditorGrid grid = GetGrid(editor);
        Control value = grid.Children.Single(c => c.IsVisible && Grid.GetColumn(c) == grid.ValueColumn && Grid.GetRow(c) == 0);
        return value is DataValidationErrors
            ? value.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PART_BackgroundBorder")
            : value;
    }
}
