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
