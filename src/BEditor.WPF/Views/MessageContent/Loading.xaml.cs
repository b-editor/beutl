using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using BEditor.Properties;

using Reactive.Bindings;

using static BEditor.IMessage;

namespace BEditor.Views.MessageContent
{
    /// <summary>
    /// Loading.xaml の相互作用ロジック
    /// </summary>
    public partial class Loading : DialogContent
    {
        public Loading(ButtonType[] buttons)
        {
            DataContext = this;

            InitializeComponent();


            #region ボタンの追加

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

            #endregion

            foreach (Button b in stack.Children)
            {
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



        public override ButtonType DialogResult { get; protected set; }

        public override event EventHandler? ButtonClicked;
    }
}