using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls.PropertyEditors;

// Lays out color wheels so that their pads are as large as the space allows: all in a row, in two rows or in one
// column, each with its header, jog and boxes stacked above and below the pad, or beside it when the space is much
// wider than high. A single row is kept unless another layout makes the pads clearly larger, so that the wheels read
// left to right as they do in other tools. What a wheel needs besides its pad comes from measuring the first wheel
// with no limits. Each wheel sits in the middle of its cell, and a shorter last row is centered.
public sealed class ColorWheelsPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ColorWheelsPanel, double>(nameof(Spacing), 8);

    public static readonly StyledProperty<double> MaxPadSizeProperty =
        AvaloniaProperty.Register<ColorWheelsPanel, double>(nameof(MaxPadSize), 240);

    // The gap between a pad and the column beside it, which the wheel's theme also keeps.
    internal const double SideGap = 12;
    private const double MinPadSize = 32;
    // Beside the pad, the controls stand apart from the other wheels' pads, so that layout has to be clearly larger;
    // so does one that breaks the row.
    private const double SidePreference = 1.15;
    private const double RowPreference = 1.2;
    private Arrangement _arrangement = new(1, MinPadSize, false);
    private ColorGradingWheel? _chromeSource;
    private double _chromeHeight;
    private double _chromeWidth;

    static ColorWheelsPanel()
    {
        AffectsMeasure<ColorWheelsPanel>(SpacingProperty, MaxPadSizeProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MaxPadSize
    {
        get => GetValue(MaxPadSizeProperty);
        set => SetValue(MaxPadSizeProperty, value);
    }

    // The layout chosen last, for tests.
    internal int Columns => _arrangement.Columns;

    internal bool IsSideBySide => _arrangement.SideBySide;

    internal double PadSize => _arrangement.Pad;

    // The sliders can scroll above the wheels. Reserve the shortest arrangement that fits the requested pads.
    internal double MeasureHeightForPad(double width, double pad)
    {
        ColorGradingWheel[] wheels = VisibleWheels();
        if (wheels.Length == 0)
            return 0;

        MeasureChrome(wheels[0]);
        pad = Math.Min(pad, Math.Min(width, MaxPadSize));
        return Arrangements(wheels.Length, width, double.PositiveInfinity, pad)
            .Where(candidate => candidate.Pad >= pad)
            .Min(candidate => HeightFor(wheels.Length, candidate));
    }

    private void MeasureChrome(ColorGradingWheel first)
    {
        if (first != _chromeSource || !first.IsMeasureValid)
        {
            first.Measure(Size.Infinity);
            _chromeHeight = first.ChromeHeight;
            _chromeWidth = first.ChromeWidth;
            _chromeSource = first;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ColorGradingWheel[] wheels = VisibleWheels();
        if (wheels.Length == 0)
            return default;

        double spacing = Spacing;
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (wheels.Length * (MaxPadSize + spacing)) - spacing;

        MeasureChrome(wheels[0]);
        _arrangement = Choose(wheels.Length, width, availableSize.Height);

        Size wheelSize = WheelSize(_arrangement);
        foreach (ColorGradingWheel wheel in wheels)
        {
            wheel.IsSideBySide = _arrangement.SideBySide;
            wheel.Measure(wheelSize);
        }

        // The cells share the whole width; without one, the wheels side by side are as wide as the row gets.
        double desiredWidth = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : (_arrangement.Columns * wheelSize.Width) + ((_arrangement.Columns - 1) * spacing);
        return new Size(desiredWidth, HeightFor(wheels.Length, _arrangement));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ColorGradingWheel[] wheels = VisibleWheels();
        if (wheels.Length == 0)
            return finalSize;

        double spacing = Spacing;
        int columns = _arrangement.Columns;
        double cellWidth = CellWidth(finalSize.Width);
        Size wheelSize = WheelSize(_arrangement);
        double wheelWidth = Math.Min(wheelSize.Width, cellWidth);
        int rows = RowCount(wheels.Length, columns);
        for (int i = 0; i < wheels.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            int inRow = row == rows - 1 ? wheels.Length - (row * columns) : columns;
            double rowOffset = (columns - inRow) * (cellWidth + spacing) / 2;
            double x = rowOffset + (column * (cellWidth + spacing)) + ((cellWidth - wheelWidth) / 2);
            double y = row * (wheelSize.Height + spacing);
            wheels[i].Arrange(new Rect(x, y, wheelWidth, wheelSize.Height));
        }

        return finalSize;
    }

    private ColorGradingWheel[] VisibleWheels() => [.. Children.OfType<ColorGradingWheel>().Where(wheel => wheel.IsVisible)];

    private static int RowCount(int count, int columns) => (count + columns - 1) / columns;

    private double HeightFor(int count, Arrangement arrangement)
    {
        int rows = RowCount(count, arrangement.Columns);
        return (rows * WheelSize(arrangement).Height) + ((rows - 1) * Spacing);
    }

    private double CellWidth(double width)
        => Math.Max((width - (Spacing * (_arrangement.Columns - 1))) / _arrangement.Columns, 0);

    private Size WheelSize(Arrangement arrangement)
    {
        double pad = arrangement.Pad;
        return arrangement.SideBySide
            ? new Size(pad + SideGap + _chromeWidth, Math.Max(pad, _chromeHeight))
            : new Size(Math.Max(pad, _chromeWidth), pad + _chromeHeight);
    }

    private Arrangement Choose(int count, double width, double height)
    {
        Arrangement? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (Arrangement candidate in Arrangements(count, width, height, MaxPadSize))
        {
            // Earlier arrangements, with more columns, win ties.
            int rows = RowCount(count, candidate.Columns);
            double score = candidate.Pad / (candidate.SideBySide ? SidePreference : 1) / (rows > 1 ? RowPreference : 1);
            if (score > bestScore + 0.5)
            {
                best = candidate;
                bestScore = score;
            }
        }

        Arrangement chosen = best ?? new Arrangement(1, MinPadSize, false);
        return chosen with { Pad = Math.Max(chosen.Pad, MinPadSize) };
    }

    private IEnumerable<Arrangement> Arrangements(int count, double width, double height, double maxPad)
    {
        foreach (int columns in new[] { count, (count + 1) / 2, 1 }.Distinct())
        {
            int rows = RowCount(count, columns);
            double cellWidth = (width - (Spacing * (columns - 1))) / columns;
            double cellHeight = double.IsFinite(height)
                ? (height - (Spacing * (rows - 1))) / rows
                : double.PositiveInfinity;

            // A wheel narrower than its controls would cut them, so only one column may be that narrow.
            if (cellWidth >= _chromeWidth || columns == 1)
                yield return new Arrangement(columns, Math.Min(Math.Min(cellWidth, cellHeight - _chromeHeight), maxPad), false);

            if (cellHeight >= _chromeHeight)
            {
                double pad = Math.Min(Math.Min(cellHeight, cellWidth - SideGap - _chromeWidth), maxPad);
                yield return new Arrangement(columns, pad, true);
            }
        }
    }

    private readonly record struct Arrangement(int Columns, double Pad, bool SideBySide);
}
