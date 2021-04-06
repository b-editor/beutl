using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using BEditor.Properties;

using Reactive.Bindings;

using static BEditor.IMessage;

namespace BEditor.Views.DialogContent
{
    public class Loading : UserControl, IDialogContent
    {
        public Loading(ButtonType[] buttons)
        {
            DataContext = this;

            InitializeComponent();

            var stack = this.FindControl<VirtualizingStackPanel>("stack");
            #region ƒ{ƒ^ƒ“‚Ì’Ç‰Á

            foreach (var button in buttons)
            {
                var text = button switch
                {
                    ButtonType.Ok => Strings.OK,
                    ButtonType.Yes => Strings.Yes,
                    ButtonType.No => Strings.No,
                    ButtonType.Cancel => Strings.Cancel,
                    ButtonType.Retry => Strings.Retry,
                    ButtonType.Close => Strings.Close,
                    _ => string.Empty,
                };

                var button_ = new Button
                {
                    Background = Brushes.Transparent,
                    Content = text,
                    CommandParameter = button,
                    Margin = new Thickness(5, 0, 5, 0),
                };

                stack.Children.Add(button_);
            }

            #endregion

            for (var i = 0; i < stack.Children.Count; i++)
            {
                var b = (Button)stack.Children[i];
                b.Click += (sender, e) =>
                {
                    DialogResult = (ButtonType)b.CommandParameter;
                    ButtonClicked?.Invoke(sender, e);
                };
            }
        }

        public Loading()
        {
            DataContext = this;

            InitializeComponent();
        }

        public ReactiveProperty<string> Text { get; } = new();
        public ReactiveProperty<bool> IsIndeterminate { get; } = new() { Value = false };

        public ReactiveProperty<int> Maximum { get; } = new() { Value = 0 };
        public ReactiveProperty<int> Minimum { get; } = new() { Value = 0 };
        public ReactiveProperty<int> NowValue { get; } = new() { Value = 0 };

        public ButtonType DialogResult { get; private set; }

        public event EventHandler? ButtonClicked;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
