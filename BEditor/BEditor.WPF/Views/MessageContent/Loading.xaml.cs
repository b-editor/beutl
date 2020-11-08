using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BEditor.ViewModels.Helper;

using BEditor.Core.Extensions.ViewCommand;

using Resource = BEditor.Core.Properties.Resources;

namespace BEditor.Views.MessageContent {
    /// <summary>
    /// Loading.xaml の相互作用ロジック
    /// </summary>
    public partial class Loading : DialogContent {
        public Loading(ButtonType[] buttons) {
            InitializeComponent();

            DataContext = this;


            #region ボタンの追加

            foreach (var button in buttons) {
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

                var button_ = new Button() {
                    Style = (Style)FindResource("MaterialDesignFlatButton"),
                    Content = text,
                    CommandParameter = button,
                    Margin = new Thickness(5, 0, 5, 0),
                    Foreground = (Brush)FindResource("MaterialDesignBody")
                };

                stack.Children.Add(button_);
            }

            #endregion

            foreach (Button b in stack.Children) {
                b.Click += (sender, e) => {
                    DialogResult = (ButtonType)b.CommandParameter;
                    ButtonClicked?.Invoke(sender, e);
                };
            }
        }

        public Loading() {
            InitializeComponent();

            DataContext = this;
        }

        public DelegateProperty<string> Text { get; } = new DelegateProperty<string>();
        public DelegateProperty<bool> IsIndeterminate { get; } = new DelegateProperty<bool>() { Value = false };

        public DelegateProperty<int> Maximum { get; } = new DelegateProperty<int>() { Value = 0 };
        public DelegateProperty<int> Minimum { get; } = new DelegateProperty<int>() { Value = 0 };
        public DelegateProperty<int> NowValue { get; } = new DelegateProperty<int>() { Value = 0 };



        public override ButtonType DialogResult { get; protected set; }

        public override event EventHandler ButtonClicked;
    }
}
