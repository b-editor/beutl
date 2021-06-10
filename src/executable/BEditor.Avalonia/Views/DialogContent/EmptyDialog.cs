
using Avalonia.Controls;
using Avalonia.Media;

using BEditor.ViewModels;

namespace BEditor.Views.DialogContent
{
    public sealed class EmptyDialog : Window
    {
        private readonly ExperimentalAcrylicBorder _border;

        public EmptyDialog(IDialogContent content) : this()
        {
            _border.Child = content;

            content.ButtonClicked += (_, _) => Close();
        }

        public EmptyDialog(IControl content) : this()
        {
            _border.Child = content;
        }

        private EmptyDialog()
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
            TransparencyLevelHint = WindowTransparencyLevel.AcrylicBlur;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _border = new ExperimentalAcrylicBorder
            {
                Material = new ExperimentalAcrylicMaterial
                {
                    TintColor = Color.FromUInt32(0xff222222),
                    TintOpacity = (double)App.Current.FindResource("AcrylicTintOpacity1")!,
                    MaterialOpacity = (double)App.Current.FindResource("AcrylicMaterialOpacity1")!
                }
            };

            Content = _border;

            PointerPressed += EmptyDialog_PointerPressed;
        }

        private void EmptyDialog_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);
        }
    }
}