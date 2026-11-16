using System.Runtime.InteropServices;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;

using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Testing.Headless;

using RoyalTerminal.Avalonia.Controls;

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
            // Use a known contrast for the pixel measurements, independent of the application theme.
            terminal.DefaultForeground = Colors.White;
            terminal.DefaultBackground = Colors.Black;
            // End on a new line so the cursor does not sit after the last bar.
            terminal.WriteOutput(Encoding.UTF8.GetBytes(string.Join("\r\n", s_rows) + "\r\n"));
            HeadlessTestHelpers.Render();

            int[] barColumns = Enumerable.Range(0, s_rows.Length).Select(row =>
            {
                var line = terminal.Screen!.GetRow(row);
                return Enumerable.Range(0, line.Columns).First(x => line[x].Codepoint == '|');
            }).ToArray();
            Assert.That(barColumns, Is.All.EqualTo(12), "the terminal must place every bar in one column");

            HeadlessCapture.SaveIfRequested(view, nameof(TerminalRenderingTests), "glyph-grid");
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            using ILockedFramebuffer pixels = frame!.Lock();
            Assert.That(pixels.Format, Is.EqualTo(PixelFormat.Bgra8888).Or.EqualTo(PixelFormat.Rgba8888));

            double rowHeight = terminal.Renderer!.CellHeight;
            Point origin = terminal.TranslatePoint(default, window)!.Value;
            int right = (int)(origin.X + terminal.Bounds.Width) - 1;

            // The bar is the last glyph of every row, so each row's rightmost lit pixel is the bar's edge.
            int[] barEdges = Enumerable.Range(0, s_rows.Length).Select(row =>
            {
                int top = (int)Math.Ceiling(origin.Y + row * rowHeight) + 1;
                int bottom = (int)Math.Floor(origin.Y + (row + 1) * rowHeight) - 1;
                return Enumerable.Range((int)origin.X, right - (int)origin.X + 1).Reverse().FirstOrDefault(x =>
                    Enumerable.Range(top, bottom - top + 1).Any(y =>
                        (Marshal.ReadInt32(pixels.Address, y * pixels.RowBytes + x * 4) & 0xFF) > 0x20), -1);
            }).ToArray();

            double barLeft = origin.X + barColumns[0] * terminal.Renderer.CellWidth;
            Assert.That(barEdges, Is.All.InRange((int)barLeft, (int)Math.Ceiling(barLeft + terminal.Renderer.CellWidth)),
                "the last glyph should be visible inside its cell");
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
