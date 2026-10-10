using System.Globalization;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;

using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Testing.Headless;

using Iciclecreek.Terminal;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TerminalRenderingTests
{
    // Every row ends with "|" in the same column. The middle rows use symbols that TUIs such as
    // Claude Code and Codex print and monospace fonts usually lack, plus full-width Japanese.
    private static readonly string[] s_rows =
    [
        "0123456789AB|",
        "╭──────────╮|",
        "⏺ Read(file)|",
        "  ⎿  Done   |",
        "✻ ❯ ✓ ⚠ ▶ ★ |",
        "日本語テキス|",
    ];

    [AvaloniaTest]
    public void Glyphs_from_fallback_fonts_stay_on_the_cell_grid()
    {
        using var view = new TerminalTabView();
        TerminalControl terminal = view.FindControl<TerminalControl>("Terminal")!;
        var window = new Window { Content = view, Width = 400, Height = 200, Background = Brushes.Black };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            // Set after the view is attached, where the XAML DynamicResource would replace it.
            terminal.Foreground = Brushes.White;
            TerminalView terminalView = terminal.GetVisualDescendants().OfType<TerminalView>().Single();
            // End on a new line so the cursor does not sit after the last bar.
            terminal.Terminal.Write(string.Join("\r\n", s_rows) + "\r\n");
            terminalView.InvalidateVisual();
            HeadlessTestHelpers.Render();

            int[] barColumns = Enumerable.Range(0, s_rows.Length).Select(row =>
            {
                var line = terminal.Terminal.Buffer.GetLine(row)!;
                return Enumerable.Range(0, line.Length).First(x => line[x].Content == "|");
            }).ToArray();
            Assume.That(barColumns, Is.All.EqualTo(barColumns[0]), "the terminal must place every bar in one column");

            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            using ILockedFramebuffer pixels = frame!.Lock();
            Assert.That(pixels.Format, Is.EqualTo(PixelFormat.Bgra8888).Or.EqualTo(PixelFormat.Rgba8888));

            double rowHeight = new FormattedText(
                "W", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(terminal.FontFamily, terminal.FontStyle, terminal.FontWeight), terminal.FontSize, Brushes.Black).Height;
            Point origin = terminalView.TranslatePoint(default, window)!.Value;
            int right = (int)(origin.X + terminalView.Bounds.Width) - 1;

            // The bar is the last glyph of every row, so each row's rightmost lit pixel is the bar's edge.
            int[] barEdges = Enumerable.Range(0, s_rows.Length).Select(row =>
            {
                int top = (int)Math.Ceiling(origin.Y + row * rowHeight) + 1;
                int bottom = (int)Math.Floor(origin.Y + (row + 1) * rowHeight) - 1;
                return Enumerable.Range((int)origin.X, right - (int)origin.X + 1).Reverse().FirstOrDefault(x =>
                    Enumerable.Range(top, bottom - top + 1).Any(y =>
                        (Marshal.ReadInt32(pixels.Address, y * pixels.RowBytes + x * 4) & 0xFF) > 0x20), -1);
            }).ToArray();

            Assert.That(barEdges, Is.All.EqualTo(barEdges[0]).Within(1),
                "a glyph from a fallback font must not shift the rest of its row off the cell grid");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
