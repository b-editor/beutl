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
    public class ColorAnimationPropertyView : UserControl
    {
        private readonly ColorAnimationProperty _property;
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

#pragma warning disable CS8618
        public ColorAnimationPropertyView()
#pragma warning restore CS8618
        {
            InitializeComponent();
            _stackPanel = this.FindControl<StackPanel>("stackPanel");
        }

        public ColorAnimationPropertyView(ColorAnimationProperty property)
        {
            DataContext = new ColorAnimationPropertyViewModel(property);
            InitializeComponent();

            _stackPanel = this.FindControl<StackPanel>("stackPanel");
            _property = property;
            property.Value.CollectionChanged += Value_CollectionChanged;

            // StackPanel‚ÉBorder‚ð’Ç‰Á
            _stackPanel.Children.AddRange(property.Value.Select((_, i) => CreateBorder(i)));

            _opencloseAnim.Children[1].Setters.Add(_heightSetter);
        }

        private Border CreateBorder(int index)
        {
            var border = new Border
            {
                [AttachmentProperty.IntProperty] = index,
                Height = 24,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(_property.Value[index].ToAvalonia()),
            };

            border.PointerPressed += Border_PointerPressed;

            return border;
        }

        private async void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                var index = AttachmentProperty.GetInt(border);

                var color = _property;
                var dialog = new ColorDialog(color);

                dialog.col.Red = color.Value[index].R;
                dialog.col.Green = color.Value[index].G;
                dialog.col.Blue = color.Value[index].B;
                dialog.col.Alpha = color.Value[index].A;

                dialog.ok_button.Click += (_, _) => _property.ChangeColor(index, Drawing.Color.FromARGB(dialog.col.Alpha, dialog.col.Red, dialog.col.Green, dialog.col.Blue)).Execute();

                await dialog.ShowDialog(App.GetMainWindow());
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

        private void Value_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                _stackPanel.Children.Add(CreateBorder(e.NewStartingIndex));
                ResetIndex();
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove)
            {
                _stackPanel.Children.RemoveAt(e.OldStartingIndex);
                ResetIndex();
            }
            else if (e.Action is NotifyCollectionChangedAction.Replace)
            {
                var num = (Border)_stackPanel.Children[e.NewStartingIndex];
                num.Background = new SolidColorBrush(_property.Value[e.NewStartingIndex].ToAvalonia());
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
                _heightSetter.Value = _property.Value.Count * 40d;

                _opencloseAnim.PlaybackDirection = PlaybackDirection.Reverse;
                await _opencloseAnim.RunAsync(this);

                Height = 40f;
            }
            else
            {
                var height = _property.Value.Count * 40d;
                _heightSetter.Value = height;

                _opencloseAnim.PlaybackDirection = PlaybackDirection.Normal;
                await _opencloseAnim.RunAsync(this);

                Height = height;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}