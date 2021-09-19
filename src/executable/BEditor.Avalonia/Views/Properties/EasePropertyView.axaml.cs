using System;
using System.Collections.Specialized;
using System.Linq;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class EasePropertyView : UserControl, IDisposable
    {
        private readonly EaseProperty _property;
        private readonly StackPanel _stackPanel;
        private readonly Setter _heightSetter = new(HeightProperty, 40d);
        private readonly Animation _opencloseAnim = new()
        {
            Duration = TimeSpan.FromSeconds(0.25),
            Children =
            {
                new()
                {
                    Cue = new(0),
                    Setters = { new Setter(HeightProperty, 40d) }
                },
                new()
                {
                    Cue = new(1),
                }
            }
        };
        private bool _isMouseDown;
        private float _oldvalue;
        private Point _startPoint;
        private int _clickCount;

#pragma warning disable CS8618
        public EasePropertyView()
#pragma warning restore CS8618
        {
            InitializeComponent();
            _stackPanel = this.FindControl<StackPanel>("stackPanel");
        }

        public EasePropertyView(EaseProperty property)
        {
            DataContext = new EasePropertyViewModel(property);
            InitializeComponent();

            _stackPanel = this.FindControl<StackPanel>("stackPanel");
            _property = property;
            property.Pairs.CollectionChanged += Pairs_CollectionChanged;

            // StackPanel‚ÉNumeric‚ð’Ç‰Á
            _stackPanel.Children.AddRange(property.Pairs.Select((_, i) => CreateNumeric(i)));

            _opencloseAnim.Children[1].Setters.Add(_heightSetter);
        }

        ~EasePropertyView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        private NumericUpDown CreateNumeric(int index)
        {
            var num = new NumericUpDown
            {
                [AttachmentProperty.IntProperty] = index,
                Value = _property.Pairs[index].Value,
                Increment = 10,
            };

            num.GotFocus += NumericUpDown_GotFocus;
            num.LostFocus += NumericUpDown_LostFocus;
            num.ValueChanged += NumericUpDown_ValueChanged;
            num.AddHandler(KeyUpEvent, NumericUpDown_KeyUp, RoutingStrategies.Tunnel);
            num.AddHandler(KeyDownEvent, NumericUpDown_KeyDown, RoutingStrategies.Tunnel);
            num.AddHandler(PointerMovedEvent, NumericUpDown_PointerMoved, RoutingStrategies.Tunnel);
            num.AddHandler(PointerReleasedEvent, NumericUpDown_PointerReleased, RoutingStrategies.Tunnel);
            num.AddHandler(PointerPressedEvent, NumericUpDown_PointerPressed, RoutingStrategies.Tunnel);
            num.AddHandler(PointerLeaveEvent, NumericUpDown_PointerLeave, RoutingStrategies.Tunnel);

            if (_property.PropertyMetadata == null) return num;

            num.FormatString = _property.PropertyMetadata.FormatString;

            if (!float.IsNaN(_property.PropertyMetadata.Max))
            {
                num.Maximum = _property.PropertyMetadata.Max;
            }

            if (!float.IsNaN(_property.PropertyMetadata.Min))
            {
                num.Minimum = _property.PropertyMetadata.Min;
            }

            return num;
        }

        private void NumericUpDown_PointerLeave(object? sender, PointerEventArgs e)
        {
            _isMouseDown = false;
            _clickCount = 0;
        }

        private void NumericUpDown_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _clickCount++;
            _isMouseDown = true;
            _startPoint = e.GetPosition(this);

            var num = (NumericUpDown)sender!;
            var index = num.GetValue(AttachmentProperty.IntProperty);

            _oldvalue = _property.Pairs[index].Value;

            if (_clickCount == 1)
            {
                e.Handled = true;
            }
            else
            {
                _clickCount = 0;
            }
        }

        private void NumericUpDown_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            _isMouseDown = false;

            var index = num.GetValue(AttachmentProperty.IntProperty);
            var newValue = (float)num.Value;

            _property.Pairs[index] = _property.Pairs[index].WithValue(_oldvalue);

            if (newValue != _oldvalue)
                _property.ChangeValue(index, newValue).Execute();
        }

        private void NumericUpDown_PointerMoved(object? sender, PointerEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            if (!num.IsKeyboardFocusWithin && _isMouseDown)
            {
                var point = e.GetPosition(this);
                var move = point - _startPoint;

                num.Value += move.X;

                _startPoint = point;
                _clickCount = 0;
            }
        }

        private void ResetIndex()
        {
            for (var i = 0; i < _stackPanel.Children.Count; i++)
            {
                if (_stackPanel.Children[i] is AvaloniaObject obj)
                {
                    obj.SetValue(AttachmentProperty.IntProperty, i);
                }
            }
        }

        private void Pairs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                _stackPanel.Children.Insert(e.NewStartingIndex, CreateNumeric(e.NewStartingIndex));
                ResetIndex();
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove)
            {
                _stackPanel.Children.RemoveAt(e.OldStartingIndex);
                ResetIndex();
            }
            else if (e.Action is NotifyCollectionChangedAction.Replace)
            {
                var num = (NumericUpDown)_stackPanel.Children[e.NewStartingIndex];
                num.Value = _property.Pairs[e.NewStartingIndex].Value;
            }
        }

        public async void ShowEasingProperty(object s, RoutedEventArgs e)
        {
            var dialog = new EasingPropertyView(DataContext!);
            await dialog.ShowDialog((Window)this.GetVisualRoot());
        }

        public async void ListToggleClick(object? sender, RoutedEventArgs? e)
        {
            var togglebutton = (ToggleButton)sender!;

            //ŠJ‚­
            if (!togglebutton.IsChecked ?? false)
            {
                _heightSetter.Value = _property.Pairs.Count * 40d;

                _opencloseAnim.PlaybackDirection = PlaybackDirection.Reverse;
                await _opencloseAnim.RunAsync(this);

                Height = 40f;
            }
            else
            {
                var height = _property.Pairs.Count * 40d;
                _heightSetter.Value = height;

                _opencloseAnim.PlaybackDirection = PlaybackDirection.Normal;
                await _opencloseAnim.RunAsync(this);

                Height = height;
            }
        }

        public void NumericUpDown_GotFocus(object? sender, GotFocusEventArgs e)
        {
            var num = (NumericUpDown)sender!;

            var index = num.GetValue(AttachmentProperty.IntProperty);

            _oldvalue = _property.Pairs[index].Value;
        }

        public void NumericUpDown_LostFocus(object? sender, RoutedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            var index = num.GetValue(AttachmentProperty.IntProperty);
            var newValue = (float)num.Value;

            _property.Pairs[index] = _property.Pairs[index].WithValue(_oldvalue);

            if (newValue != _oldvalue)
                _property.ChangeValue(index, newValue).Execute();
        }

        public async void NumericUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            var index = num.GetValue(AttachmentProperty.IntProperty);

            _property.Pairs[index] = _property.Pairs[index].WithValue(_property.Clamp((float)e.NewValue));

            await (AppModel.Current.Project!).PreviewUpdateAsync(_property.GetParent<ClipElement>()!);
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

        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            DataContext = null;
            GC.SuppressFinalize(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}