using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    private readonly StackPanel _progress;
    private readonly StackPanel _result;
    private readonly NumericUpDown _bpm;
    private readonly NumericUpDown _offset;
    private readonly Button _apply;
    private readonly Button _cancel;

    public BpmEstimateView(BpmGridOptions current)
    {
        _current = current;
        _ring = new ProgressRing
        {
            Width = 20,
            Height = 20,
            IsIndeterminate = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _status = new TextBlock
        {
            Text = Strings.BpmEstimate_Analyzing,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _progress = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _ring, _status },
        };
        _bpm = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 999,
            Increment = 0.1m,
            FormatString = "F1",
            MinWidth = 180,
        };
        _offset = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 600,
            Increment = 0.001m,
            FormatString = "F3",
            MinWidth = 180,
        };
        _apply = new Button
        {
            Content = Strings.Apply,
            MinWidth = 96,
            IsVisible = false,
        };
        _apply.Classes.Add("accent");
        _apply.Click += (_, _) =>
        {
            if (_bpm.Value is { } bpm && _offset.Value is { } offset)
            {
                Applied?.Invoke(this, BpmEstimateService.ApplyTo(_current, (double)bpm, (double)offset));
            }
        };
        _cancel = new Button { Content = Strings.Cancel, MinWidth = 96 };
        _cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { _apply, _cancel },
        };
        _result = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new TextBlock { Text = Strings.BpmValue },
                _bpm,
                new TextBlock { Text = Strings.BpmEstimate_FirstBeatOffset },
                _offset,
            },
        };

        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(4),
            Children =
            {
                new TextBlock
                {
                    Text = Strings.BpmEstimate_Title,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                _progress,
                _result,
                buttons,
            },
        };
    }

    public event EventHandler<BpmGridOptions>? Applied;

    public event EventHandler? Cancelled;

    internal NumericUpDown BpmInput => _bpm;

    internal NumericUpDown OffsetInput => _offset;

    internal Button ApplyButton => _apply;

    internal Button CancelButton => _cancel;

    public void ShowAnalyzing()
    {
        _ring.IsIndeterminate = true;
        _progress.IsVisible = true;
        _status.Text = Strings.BpmEstimate_Analyzing;
        _result.IsVisible = false;
        _apply.IsVisible = false;
        _cancel.Content = Strings.Cancel;
    }

    public void ShowProgress(int percent)
    {
        _status.Text = $"{Strings.BpmEstimate_Analyzing} {percent}%";
    }

    public void ShowResult(double bpm, double firstBeatOffsetSeconds)
    {
        _ring.IsIndeterminate = false;
        _progress.IsVisible = false;
        _bpm.Value = (decimal)Math.Round(bpm, 1, MidpointRounding.AwayFromZero);
        // The box shows milliseconds; keep the estimate's precision so Apply uses what was measured.
        _offset.Value = (decimal)firstBeatOffsetSeconds;
        _result.IsVisible = true;
        _apply.IsVisible = true;
        _cancel.Content = Strings.Cancel;
    }

    public void ShowError(string message)
    {
        _ring.IsIndeterminate = false;
        _progress.IsVisible = true;
        _status.Text = message;
        _result.IsVisible = false;
        _apply.IsVisible = false;
        _cancel.Content = Strings.Close;
    }
}
