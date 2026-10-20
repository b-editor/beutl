using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

public partial class AcceptLicenseTask : UserControl
{
    private readonly ILogger _logger = Log.CreateLogger<AcceptLicenseTask>();

    public AcceptLicenseTask()
    {
        InitializeComponent();
    }

    private void ShowLicenseDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: LicenseItem item })
        {
            Uri? url = item.License.LicenseUrl;

            try
            {
                if (url != null)
                {
                    Process.Start(new ProcessStartInfo(url.ToString())
                    {
                        UseShellExecute = true,
                        Verb = "open"
                    });

                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not open URL. {URL}", url);
            }
        }
    }
}
