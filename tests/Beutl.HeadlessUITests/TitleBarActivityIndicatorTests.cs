using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TitleBarActivityIndicatorTests
{
    private const double Turn = TitleBarActivityIndicator.TurnSeconds;

    [AvaloniaTest]
    public void Running_tasks_take_turns_in_order()
    {
        var indicator = new TitleBarActivityIndicator
        {
            Activities = TitleBarActivities.Startup | TitleBarActivities.Output | TitleBarActivities.AiJob,
        };

        indicator.Advance(0);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.Output));
        indicator.Advance(Turn - 0.1);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.Output));
        // Each turn is checked a little after it is due, as a frame would see it.
        indicator.Advance(Turn + 0.01);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.AiJob));
        indicator.Advance((2 * Turn) + 0.02);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.Startup));
        indicator.Advance((3 * Turn) + 0.03);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.Output));
    }

    [AvaloniaTest]
    public void A_task_that_ends_hands_over_at_once_and_the_idle_icon_stops_the_frames()
    {
        var indicator = new TitleBarActivityIndicator
        {
            Activities = TitleBarActivities.Output | TitleBarActivities.AiJob,
        };
        indicator.Advance(0);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.Output));

        indicator.Activities = TitleBarActivities.AiJob;
        indicator.Advance(0.5);
        Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.AiJob));

        indicator.Activities = TitleBarActivities.None;
        indicator.Advance(1);
        Assert.Multiple(() =>
        {
            Assert.That(indicator.Shown, Is.EqualTo(TitleBarActivities.None));
            Assert.That(indicator.IsAnimating, Is.True, "the last task still fades out");
        });

        indicator.Advance(1 + TitleBarActivityIndicator.FadeSeconds);
        Assert.That(indicator.IsAnimating, Is.False);
    }

    [AvaloniaTest]
    public void Each_task_draws_its_own_picture()
    {
        (string Name, TitleBarActivities Activities, double? Progress, bool Installing)[] states =
        [
            ("idle", TitleBarActivities.None, null, false),
            ("startup", TitleBarActivities.Startup, null, false),
            ("output", TitleBarActivities.Output, null, false),
            ("AI job", TitleBarActivities.AiJob, null, false),
            ("download of unknown size", TitleBarActivities.Update, null, false),
            ("download at 40%", TitleBarActivities.Update, 0.4, false),
            ("install", TitleBarActivities.Update, null, true),
        ];
        var indicators = states
            .Select(state =>
            {
                TitleBarActivityIndicator indicator = CreateIndicator(state.Activities, state.Progress);
                indicator.IsUpdateInstalling = state.Installing;
                return indicator;
            })
            .ToArray();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.AddRange(indicators.Select(Cell));
        var window = new Window
        {
            Width = 32 * states.Length,
            Height = 32,
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = row,
        };
        try
        {
            window.Show();
            RunTo(indicators, _ => 0);
            RunTo(indicators, _ => 0.9);

            var frame = HeadlessTestHelpers.CaptureFramePixels(window);
            byte[][] pictures = Enumerable.Range(0, states.Length)
                .Select(i => Crop(frame, new PixelRect(i * 32, 0, 32, 32)))
                .ToArray();
            for (int i = 0; i < pictures.Length; i++)
            {
                for (int j = i + 1; j < pictures.Length; j++)
                {
                    Assert.That(pictures[i], Is.Not.EqualTo(pictures[j]), $"{states[i].Name} and {states[j].Name} look the same");
                }
            }
        }
        finally
        {
            window.Close();
        }

        if (Environment.GetEnvironmentVariable("BEUTL_TITLEBAR_CAPTURE") is { } directory)
        {
            foreach (ThemeVariant theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (double scale in new[] { 1d, 4d })
                    CaptureSheet(directory, theme, scale);
            }
        }
    }

    // A sheet of moments for eyeballing the animations: a row per state, a column per moment.
    private static void CaptureSheet(string directory, ThemeVariant theme, double scale)
    {
        (string Name, TitleBarActivities Activities, double? Progress, bool Installing, double Start, double Step)[] rows =
        [
            ("idle", TitleBarActivities.None, null, false, 0.5, 0.2),
            ("startup", TitleBarActivities.Startup, null, false, 0.5, 0.2),
            ("output", TitleBarActivities.Output, null, false, 0.5, 0.33),
            ("ai", TitleBarActivities.AiJob, null, false, 0.5, 0.2),
            ("update", TitleBarActivities.Update, null, false, 0.5, 0.15),
            ("update-40", TitleBarActivities.Update, 0.4, false, 0.5, 0.2),
            ("installing", TitleBarActivities.Update, null, true, 0.5, 0.2),
            ("handover", TitleBarActivities.Output | TitleBarActivities.AiJob, null, false, Turn - 0.1, 0.06),
        ];
        const int columns = 8;
        var cells = new List<(TitleBarActivityIndicator Indicator, double Time)>();
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions(string.Join(",", rows.Select(_ => "32"))),
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("32", columns))),
        };
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                var row = rows[r];
                TitleBarActivityIndicator indicator = CreateIndicator(row.Activities, row.Progress);
                indicator.IsUpdateInstalling = row.Installing;
                Border cell = Cell(indicator);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
                cells.Add((indicator, row.Start + (c * row.Step)));
            }
        }

        var window = new Window
        {
            Width = columns * 32 * scale,
            Height = rows.Length * 32 * scale,
            RequestedThemeVariant = theme,
            Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(scale, scale), Child = grid },
        };
        try
        {
            window.Show();
            TitleBarActivityIndicator[] indicators = cells.Select(cell => cell.Indicator).ToArray();
            RunTo(indicators, _ => 0);
            RunTo(indicators, i => Math.Min(cells[i].Time, Turn));
            RunTo(indicators, i => cells[i].Time);
            using var frame = window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(directory, $"titlebar-activity-{theme}-{scale}x.png"));
        }
        finally
        {
            window.Close();
        }
    }

    private static TitleBarActivityIndicator CreateIndicator(TitleBarActivities activities, double? progress)
    {
        var indicator = new TitleBarActivityIndicator { Activities = activities, UpdateProgress = progress };
        indicator.Bind(TitleBarActivityIndicator.AccentProperty, indicator.GetResourceObservable("AccentFillColorDefaultBrush"));
        return indicator;
    }

    private static Border Cell(TitleBarActivityIndicator indicator)
    {
        return new Border { Width = 32, Height = 32, Child = indicator };
    }

    // Puts every indicator on its own clock and lets a frame draw it.
    private static void RunTo(TitleBarActivityIndicator[] indicators, Func<int, double> time)
    {
        for (int i = 0; i < indicators.Length; i++)
            indicators[i].FixedTime = time(i);

        HeadlessTestHelpers.Render(2);
    }

    private static byte[] Crop((byte[] Pixels, int RowBytes, PixelSize Size) frame, PixelRect rect)
    {
        var bytes = new byte[rect.Width * rect.Height * 4];
        for (int y = 0; y < rect.Height; y++)
        {
            Array.Copy(frame.Pixels, ((rect.Y + y) * frame.RowBytes) + (rect.X * 4), bytes, y * rect.Width * 4, rect.Width * 4);
        }

        return bytes;
    }
}
