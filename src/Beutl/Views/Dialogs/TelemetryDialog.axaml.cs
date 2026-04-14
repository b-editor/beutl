using Avalonia.Interactivity;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public sealed partial class TelemetryDialog : FAContentDialog
{
    public TelemetryDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private void ShowDetail_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://beutl.beditor.net/about/telemetry")
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }
}
