using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media;

using BEditor.ViewModels;

namespace BEditor.Views.DialogContent
{
    public class EmptyDialog : Window
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
                    TintOpacity = MainWindowViewModel.Current.AcrylicTintOpacity1,
                    MaterialOpacity = MainWindowViewModel.Current.AcrylicMaterialOpacity1
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