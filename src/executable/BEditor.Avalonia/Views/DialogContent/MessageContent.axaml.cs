using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using BEditor.Properties;

using static BEditor.IMessage;

namespace BEditor.Views.DialogContent
{
    public sealed class MessageContent : UserControl, IDialogContent
    {
        public MessageContent()
        {
            InitializeComponent();
        }

        public MessageContent(ButtonType[]? buttons, object content, IconType iconKind)
        {
            InitializeComponent();

            buttons ??= new[]
            {
                ButtonType.Ok,
                ButtonType.Close
            };

            var stack = this.FindControl<StackPanel>("stack");
            var label = this.FindControl<Label>("label");

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

            foreach (Button b in stack.Children)
            {
                b.Click += (sender, e) =>
                {
                    DialogResult = (ButtonType)b.CommandParameter;
                    ButtonClicked?.Invoke(sender, e);
                };
            }

            if (iconKind != IconType.None)
            {
                //icon.Content = new PackIcon()
                //{
                //    Kind = (PackIconKind)Enum.ToObject(typeof(PackIconKind), (int)iconKind),
                //    HorizontalAlignment = HorizontalAlignment.Center,
                //    VerticalAlignment = VerticalAlignment.Center,
                //    Width = 60,
                //    Height = 60
                //};
            }

            label.Content = content;
        }

        public ButtonType DialogResult { get; private set; }

        public event EventHandler? ButtonClicked;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}