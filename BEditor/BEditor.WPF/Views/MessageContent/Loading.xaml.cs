using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using BEditor.Core.Extensions.ViewCommand;

using Resource = BEditor.Core.Properties.Resources;
using Reactive.Bindings;

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

        public override event EventHandler ButtonClicked;
    }
}
