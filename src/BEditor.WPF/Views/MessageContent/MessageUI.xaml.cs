using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using MaterialDesignThemes.Wpf;

using static BEditor.IMessage;

using Resource = BEditor.Properties.Resources;

namespace BEditor.Views.MessageContent
{
    /// <summary>
    /// MessageUI.xaml の相互作用ロジック
    /// </summary>
    public partial class MessageUI : DialogContent
    {
        public MessageUI(ButtonType[]? buttons, object content, IconType iconKind)
        {
            InitializeComponent();

            buttons ??= new ButtonType[]
            {
                ButtonType.Ok,
                ButtonType.Close
            };
            foreach (var button in buttons)
            {
                var text = button switch
                {
                    ButtonType.Ok => Resource.OK,
                    ButtonType.Yes => Resource.Yes,
                    ButtonType.No => Resource.No,
                    ButtonType.Cancel => Resource.Cancel,
                    ButtonType.Retry => Resource.Retry,
                    ButtonType.Close => Resource.Close,
                    _ => "",
                };

                var button_ = new Button()
                {
                    Style = (Style)FindResource("MaterialDesignFlatButton"),
                    Content = text,
                    CommandParameter = button,
                    Margin = new Thickness(5, 0, 5, 0),
                    Foreground = (Brush)FindResource("MaterialDesignBody")
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
                icon.Content = new PackIcon()
                {
                    Kind = (PackIconKind)Enum.ToObject(typeof(PackIconKind), (int)iconKind),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 60,
                    Height = 60
                };
            }

            label.Content = content;
        }

        public override ButtonType DialogResult { get; protected set; }

        public override event EventHandler? ButtonClicked;
    }
}
