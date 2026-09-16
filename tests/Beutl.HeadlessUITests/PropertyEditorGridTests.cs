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
    public void Disabling_an_attached_scope_restores_the_ordinary_layout()
    {
        var number = new NumberEditor<float> { Header = "Width", Value = 640 };
        var alignment = new AlignmentXEditor { Header = "Alignment X" };
        var scope = new StackPanel { Children = { number, alignment } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = 760, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(GetGrid(number).ColumnDefinitions[0].Width.IsAbsolute, Is.True);
            Assert.That(GetBox(alignment).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Left));

            PropertyEditorGrid.SetIsAlignmentScope(scope, false);
            HeadlessTestHelpers.Render(3);
            AssertRestored();

            // A disabled scope must no longer drive its previously aligned descendants.
            PropertyEditorGrid.SetValueColumnRatio(scope, .7);
            window.Width = 900;
            HeadlessTestHelpers.Render(3);
            AssertRestored();
        }
        finally { window.Close(); }

        void AssertRestored()
        {
            Assert.That(GetGrid(number).ColumnDefinitions[0].Width.IsStar, Is.True);
            Assert.That(GetGrid(alignment).ColumnDefinitions[0].Width.IsStar, Is.True);
            Assert.That(GetBox(alignment).HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
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
