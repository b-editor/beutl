using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.Properties;

namespace BEditor.Views.Properties
{
    public sealed class GradientEditor : UserControl
    {
        private readonly GradientProperty _property;
        private readonly int _index;
        private readonly NumericUpDown _num;
        private readonly Border _border;
        private float _oldvalue;

#pragma warning disable CS8618
        public GradientEditor()
#pragma warning restore CS8618
        {
            InitializeComponent();
        }

        public GradientEditor(GradientProperty property, int index)
        {
            _property = property;
            _index = index;
            InitializeComponent();

            _num = this.FindControl<NumericUpDown>("Numeric");
            _border = this.FindControl<Border>("border");
            _num.AddHandler(KeyUpEvent, NumericUpDown_KeyUp, RoutingStrategies.Tunnel);
            _num.AddHandler(KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);

            _num.Value = property.KeyPoints[index].Position * 100;
            _border.Background = new SolidColorBrush(property.KeyPoints[index].Color.ToAvalonia());
        }

        public void NumericUpDown_GotFocus(object? sender, GotFocusEventArgs e)
        {
            _oldvalue = _property.KeyPoints[_index].Position * 100;
        }

        public void NumericUpDown_LostFocus(object? sender, RoutedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            var newValue = num.Value;

            _property.KeyPoints[_index] = new(_property.KeyPoints[_index].Color, _oldvalue / 100);

            _property.UpdatePoint(_index, new(_property.KeyPoints[_index].Color, (float)newValue / 100));
        }

        public async void NumericUpDown_ValueChanged(object sender, NumericUpDownValueChangedEventArgs e)
        {
            var newValue = (float)e.NewValue / 100;
            var oldValue = _property.KeyPoints[_index].Position;
            if (newValue != oldValue)
                _property.KeyPoints[_index] = new(_property.KeyPoints[_index].Color, newValue);

            await (AppModel.Current.Project!).PreviewUpdateAsync(_property.GetParent<ClipElement>()!);
        }

        public void Remove_Click(object s, RoutedEventArgs e)
        {
            if (_property.KeyPoints.Count == 2)
            {
                AppModel.Current.Message.Snackbar(Strings.CannotDeleteAnyMore, string.Empty, IMessage.IconType.Warning);
            }
            else
            {
                _property.RemovePoint(_property.KeyPoints[_index]).Execute();
            }
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnAttachedToLogicalTree(e);
            _property.KeyPoints.CollectionChanged += KeyPoints_CollectionChanged;

            var value = _property.KeyPoints[_index].Position * 100;

            if (_num.Value != value)
                _num.Value = value;

            if (_border.Background is SolidColorBrush brush)
            {
                brush.Color = _property.KeyPoints[_index].Color.ToAvalonia();
            }
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromLogicalTree(e);
            _property.KeyPoints.CollectionChanged -= KeyPoints_CollectionChanged;

            var value = _property.KeyPoints[_index].Position * 100;

            if (_num.Value != value)
                _num.Value = value;

            if (_border.Background is SolidColorBrush brush)
            {
                brush.Color = _property.KeyPoints[_index].Color.ToAvalonia();
            }
        }

        private void KeyPoints_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace &&
                e.NewStartingIndex == _index)
            {
                var value = _property.KeyPoints[_index].Position * 100;

                if (_num.Value != value)
                    _num.Value = value;

                if (_border.Background is SolidColorBrush brush)
                {
                    brush.Color = _property.KeyPoints[_index].Color.ToAvalonia();
                }
            }
        }

        private async void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                var item = _property.KeyPoints[_index];
                var dialog = new ColorDialog();

                dialog.col.Color = item.Color.ToAvalonia();
                dialog.col.PreviousColor = dialog.col.Color;

                dialog.Command = (d) =>
                {
                    var color = Drawing.Color.FromArgb(d.col.Color.A, d.col.Color.R, d.col.Color.G, d.col.Color.B);
                    _property.UpdatePoint(_index, new GradientKeyPoint(color, _property.KeyPoints[_index].Position)).Execute();
                };

                await dialog.ShowDialog(App.GetMainWindow());
            }
        }

        private void NumericUpDown_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift && sender is NumericUpDown numeric)
            {
                numeric.Increment = 1;
            }
        }

        private void NumericUpDown_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift && sender is NumericUpDown numeric)
            {
                numeric.Increment = 10;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
