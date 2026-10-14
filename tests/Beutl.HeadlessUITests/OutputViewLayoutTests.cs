using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

// Checks only that OutputView's input columns are star-sized and grow with the width. Capturing a frame of
// this view crashes the software Vulkan host, so no pixels are read.
[TestFixture]
public class OutputViewLayoutTests
{
    private static List<Grid> LabeledRowGrids(OutputView view)
    {
        // The labeled rows are declared 3-column grids (label / splitter / input). Walking the logical
        // tree (not the visual tree) excludes the control templates' own internal grids.
        return view.GetLogicalDescendants()
            .OfType<Grid>()
            .Where(g => g.ColumnDefinitions.Count == 3)
            .ToList();
    }

    [AvaloniaTest]
    public void Input_columns_are_flexible_not_fixed_width()
    {
        var view = new OutputView();
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            List<Grid> rows = LabeledRowGrids(view);
            Assert.That(
                rows,
                Has.Count.GreaterThanOrEqualTo(3),
                "expected the Destination / Encoder / Supersampling rows as 3-column grids");

            foreach (Grid row in rows)
            {
                ColumnDefinition input = row.ColumnDefinitions[2];
                Assert.That(
                    input.Width.IsStar,
                    Is.True,
                    "the input column must be flexible (star-sized), not a fixed width");
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Destination_input_grows_with_available_width()
    {
        var view = new OutputView();
        var window = new Window { Content = view, Width = 420, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            TextBox destination = view.GetLogicalDescendants()
                .OfType<TextBox>()
                .First(x => x.Name == "DestinationInputTextBox");
            double narrow = destination.Bounds.Width;

            window.Width = 1100;
            HeadlessTestHelpers.Render();
            double wide = destination.Bounds.Width;

            Assert.That(
                narrow,
                Is.GreaterThan(0),
                "the destination field should be arranged");
            Assert.That(
                wide,
                Is.GreaterThan(narrow + 100),
                "the destination field must widen with the pane (flexible), not stay fixed");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
