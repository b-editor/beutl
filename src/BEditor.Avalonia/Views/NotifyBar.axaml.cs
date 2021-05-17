using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace BEditor.Views
{
    public class NotifyBar : TemplatedControl
    {
        public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<NotifyBar, string>(nameof(Title), string.Empty);
        public static readonly StyledProperty<object?> ContentProperty = AvaloniaProperty.Register<NotifyBar, object?>(nameof(Content));
        private Button? _closeButton;

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public object? Content
        {
            get => GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            _closeButton = e.NameScope.Find<Button>("CloseButton");
            _closeButton.Click += CloseButton_Click;
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (Parent is Panel panel) panel.Children.Remove(this);
            else if (Parent is ContentControl ctr) ctr.Content = null;
            else if (Parent is Decorator dec) dec.Child = null;
        }
    }
}