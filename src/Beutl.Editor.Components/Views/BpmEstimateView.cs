using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Views;

// The estimate flyout's body, kept apart from the flyout so its states can be shown in a window of their own.
internal sealed class BpmEstimateView : UserControl
{
    private readonly BpmGridOptions _current;
    private readonly ProgressRing _ring;
    private readonly TextBlock _status;
    private readonly Grid _statusRow;
    private readonly StackPanel _fields;
    private readonly NumericUpDown _bpm;
    private readonly NumericUpDown _offset;

    public BpmEstimateView(BpmGridOptions current)
    {
        _current = current;
        _ring = new ProgressRing
        {
            Width = 20,
            Height = 20,
            IsIndeterminate = true,
            // Margin rather than a spacer column, so an error without the ring starts at the left edge.
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        // A grid, not a horizontal stack, so a long error wraps within the flyout's width.
        _statusRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { _ring, _status },
        };
        Grid.SetColumn(_status, 1);
        _bpm = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 999,
            Value = (decimal)current.Bpm,
            Increment = 1,
            FormatString = "F1",
            MinWidth = 140,
        };
        _offset = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 600,
            Value = (decimal)current.Offset.ToSeconds(),
            Increment = 0.01m,
            FormatString = "F3",
            MinWidth = 140,
        };
        _fields = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Strings.BpmValue },
                _bpm,
                new TextBlock { Text = Strings.BpmOffset },
                _offset,
            },
        };
        Content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(4),
            Children = { _statusRow, _fields },
        };
        ShowAnalyzing();
    }

    internal event EventHandler? StateChanged;

    internal bool CanConfirm { get; private set; }

    internal NumericUpDown BpmInput => _bpm;

    internal NumericUpDown OffsetInput => _offset;

    internal StackPanel Fields => _fields;

    internal TextBlock StatusText => _status;

    internal bool TryCreateOptions(out BpmGridOptions options)
    {
        if (!CanConfirm || _bpm.Value is not { } bpm || _offset.Value is not { } offset)
        {
            options = default;
            return false;
        }

        options = BpmEstimateService.ApplyTo(_current, (double)bpm, (double)offset);
        return true;
    }

    public void ShowAnalyzing()
    {
        CanConfirm = false;
        _ring.IsVisible = true;
        _ring.IsIndeterminate = true;
        _statusRow.IsVisible = true;
        _status.Text = Strings.BpmEstimate_Analyzing;
        _fields.IsVisible = true;
        _fields.IsEnabled = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowProgress(int percent)
    {
        _status.Text = $"{Strings.BpmEstimate_Analyzing} {percent}%";
    }

    public void ShowResult(double bpm, double firstBeatOffsetSeconds)
    {
        CanConfirm = true;
        _ring.IsIndeterminate = false;
        _statusRow.IsVisible = false;
        _bpm.Value = (decimal)Math.Round(bpm, 1, MidpointRounding.AwayFromZero);
        // The box shows milliseconds; keep the estimate's precision so confirm applies what was measured.
        _offset.Value = (decimal)firstBeatOffsetSeconds;
        _fields.IsVisible = true;
        _fields.IsEnabled = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowError(string message)
    {
        CanConfirm = false;
        _ring.IsVisible = false;
        _ring.IsIndeterminate = false;
        _statusRow.IsVisible = true;
        _status.Text = message;
        _fields.IsVisible = true;
        _fields.IsEnabled = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
