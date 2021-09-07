using System;

using Avalonia;
using Avalonia.Layout;
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
            var label = this.FindControl<ContentControl>("label");
            var icon = this.FindControl<ContentControl>("icon");

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
                    Content = text,
                    CommandParameter = button,
                };

                if (button is ButtonType.Ok or ButtonType.Yes)
                {
                    button_.Classes.Add("accent");
                }

                button_.Click += (sender, e) =>
                {
                    DialogResult = (ButtonType)((Button)sender!).CommandParameter;
                    ButtonClicked?.Invoke(sender, e);
                };

                stack.Children.Add(button_);
            }

            var geometry = iconKind switch
            {
                IconType.Info => App.Current.FindResource("Info24Regular") as Geometry,
                IconType.None => null,
                IconType.Error => App.Current.FindResource("ErrorCircle24Regular") as Geometry,
                IconType.Success => App.Current.FindResource("CheckmarkCircle24Regular") as Geometry,
                IconType.Warning => App.Current.FindResource("Warning24Regular") as Geometry,
                _ => null,
            };

            if (geometry != null)
            {
                icon.Content = new FluentAvalonia.UI.Controls.PathIcon()
                {
                    Data = geometry,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 40,
                    Height = 40
                };
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