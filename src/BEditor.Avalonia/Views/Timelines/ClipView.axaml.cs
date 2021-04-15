using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.ViewModels.Timelines;

namespace BEditor.Views.Timelines
{
    public class ClipView : UserControl
    {
        public ClipView()
        {
            InitializeComponent();
        }

        public ClipView(ClipElement clip)
        {
            var viewmodel = clip.GetCreateClipViewModel();
            DataContext = viewmodel;

            InitializeComponent();

            Height = ConstantSettings.ClipHeight;
        }

        public ClipViewModel ViewModel => (DataContext as ClipViewModel)!;

        public void Pointer_Pressed(object s, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint((IVisual)s);
            if (point.Properties.IsLeftButtonPressed)
            {
                ViewModel.PointerLeftPressed(e);
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                ViewModel.PointerRightPressed(point.Position);
            }
        }

        public void Pointer_Released(object s, PointerReleasedEventArgs e)
        {
            var point = e.GetCurrentPoint((IVisual)s);
            if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonReleased)
            {
                ViewModel.PointerLeftReleased();
            }
        }

        public void Pointer_Moved(object s, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint((IVisual)s);
            ViewModel.PointerMoved(point.Position);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}