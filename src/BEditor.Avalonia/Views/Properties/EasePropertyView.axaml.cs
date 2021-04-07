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
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class EasePropertyView : UserControl
    {
        private readonly EaseProperty _property;
        private readonly StackPanel _stackPanel;
        private readonly Setter _heightSetter = new(HeightProperty, 32.5);
        private readonly Animation _opencloseAnim = new()
        {
            Duration = TimeSpan.FromSeconds(0.25),
            Children =
            {
                new()
                {
                    Cue = new(0),
                    Setters = { new Setter(HeightProperty, 32.5) }
                },
                new()
                {
                    Cue = new(1),
                }
            }
        };
        private float _oldvalue;

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
            property.Value.CollectionChanged += Value_CollectionChanged;

            // StackPanel‚ÉNumeric‚ð’Ç‰Á
            _stackPanel.Children.AddRange(property.Value.Select((_, i) => CreateNumeric(i)));

            _opencloseAnim.Children[1].Setters.Add(_heightSetter);
        }

        private NumericUpDown CreateNumeric(int index)
        {
            var num = new NumericUpDown
            {
                [AttachmentProperty.IntProperty] = index,
                Height = 32,
                Margin = new Thickness(5, 0.25, 5, 0.25),
                VerticalAlignment = VerticalAlignment.Center,
                Value = _property.Value[index],
                ShowButtonSpinner = false,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent
            };

            num.GotFocus += NumericUpDown_GotFocus;
            num.LostFocus += NumericUpDown_LostFocus;
            num.ValueChanged += NumericUpDown_ValueChanged;

            if (_property.PropertyMetadata is null) return num;

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

        private void Value_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                _stackPanel.Children.Add(CreateNumeric(e.NewStartingIndex));
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
                num.Value = _property.Value[e.NewStartingIndex];
            }
        }

        public async void ShowEasingProperty(object s, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Content = new EasingPropertyView(DataContext!),
                SizeToContent = SizeToContent.WidthAndHeight
            };
            await dialog.ShowDialog((Window)this.GetVisualRoot());
        }

        public async void ListToggleClick(object? sender, RoutedEventArgs? e)
        {
            var togglebutton = (ToggleButton)sender!;

            //ŠJ‚­
            if (!togglebutton.IsChecked ?? false)
            {
                _heightSetter.Value = _property.Value.Count * 32.5;

                _opencloseAnim.PlaybackDirection = PlaybackDirection.Reverse;
                await _opencloseAnim.RunAsync(this);

                Height = 32.5;
            }
            else
            {
                var height = _property.Value.Count * 32.5;
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

            _oldvalue = _property.Value[index];
        }

        public void NumericUpDown_LostFocus(object? sender, RoutedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            var index = num.GetValue(AttachmentProperty.IntProperty);
            var newValue = num.Value;

            _property.Value[index] = _oldvalue;

            _property.ChangeValue(index, (float)newValue).Execute();
        }

        public void NumericUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            var num = (NumericUpDown)sender!;
            var index = num.GetValue(AttachmentProperty.IntProperty);

            _property.Value[index] = _property.Clamp((float)e.NewValue);

            AppModel.Current.Project!.PreviewUpdate(_property.GetParent2()!);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
