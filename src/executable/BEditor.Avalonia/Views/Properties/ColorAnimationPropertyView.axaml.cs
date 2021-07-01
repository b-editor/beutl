using System;
using System.Collections.Specialized;
using System.Linq;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class ColorAnimationPropertyView : UserControl, IDisposable
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
            property.Pairs.CollectionChanged += Pairs_CollectionChanged;

            // StackPanel‚ÉBorder‚ð’Ç‰Á
            _stackPanel.Children.AddRange(property.Pairs.Select((_, i) => CreateBorder(i)));

            _opencloseAnim.Children[1].Setters.Add(_heightSetter);
        }

        ~ColorAnimationPropertyView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        private Border CreateBorder(int index)
        {
            var border = new Border
            {
                [AttachmentProperty.IntProperty] = index,
                Height = 24,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(_property.Pairs[index].Value.ToAvalonia()),
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

                var pair = color.Pairs[index];
                dialog.col.Color = new Color(pair.Value.A, pair.Value.R, pair.Value.G, pair.Value.B);

                dialog.Command = (d) => _property.ChangeColor(index, Drawing.Color.FromArgb(d.col.Color.A, d.col.Color.R, d.col.Color.G, d.col.Color.B)).Execute();

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

        private void Pairs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add)
            {
                _stackPanel.Children.Insert(e.NewStartingIndex, CreateBorder(e.NewStartingIndex));
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
                num.Background = new SolidColorBrush(_property.Pairs[e.NewStartingIndex].Value.ToAvalonia());
            }
        }

        public async void ShowEasingProperty(object s, RoutedEventArgs e)
        {
            var content = new EasingPropertyView(DataContext!);
            var dialog = new Window
            {
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            await dialog.ShowDialog((Window)this.GetVisualRoot());
            content.DataContext = null;
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