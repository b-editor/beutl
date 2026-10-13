using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

// Shows an IProgress<double> report (0 to 1, or a non-finite value while unknown) in a progress bar.
public sealed class ProgressState : IProgress<double>
{
    public ReactiveProperty<double> Percent { get; } = new();

    public ReactiveProperty<bool> IsIndeterminate { get; } = new(false);

    public void Report(double value)
    {
        if (double.IsFinite(value))
        {
            Percent.Value = value * 100;
            IsIndeterminate.Value = false;
        }
        else
        {
            IsIndeterminate.Value = true;
        }
    }
}
