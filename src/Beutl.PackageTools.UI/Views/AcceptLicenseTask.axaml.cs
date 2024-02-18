using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Logging;
using Beutl.PackageTools.UI.Models;

namespace Beutl.PackageTools.UI.Views;

public partial class AcceptLicenseTask : UserControl
{
    private readonly ILogger _logger = Log.CreateLogger<AcceptLicenseTask>();
    private bool _doubleClick;

    public AcceptLicenseTask()
    {
        InitializeComponent();
    }

    private void OnTaskNamePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_doubleClick)
        {
            if (DataContext is AcceptLicenseTaskModel model)
            {
                model.ShowDetails.Value = !model.ShowDetails.Value;
            }

            _doubleClick = false;
        }
    }

    private void OnTaskNamePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _doubleClick = true;
        }
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
