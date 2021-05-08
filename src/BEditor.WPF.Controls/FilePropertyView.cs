using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BEditor.WPF.Controls
{
    public class FilePropertyView : BasePropertyView
    {
        public static readonly DependencyProperty FileProperty = DependencyProperty.Register(nameof(File), typeof(string), typeof(FilePropertyView));
        public static readonly DependencyProperty OpenFileCommandProperty = DependencyProperty.Register(nameof(OpenFileCommand), typeof(ICommand), typeof(FilePropertyView));
        public static readonly DependencyProperty ModeIndexProperty = DependencyProperty.Register(nameof(ModeIndex), typeof(int), typeof(FilePropertyView), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        static FilePropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FilePropertyView), new FrameworkPropertyMetadata(typeof(FilePropertyView)));
        }

        public ICommand OpenFileCommand
        {
            get => (ICommand)GetValue(OpenFileCommandProperty);
            set => SetValue(OpenFileCommandProperty, value);
        }
        public string File
        {
            get => (string)GetValue(FileProperty);
            set => SetValue(FileProperty, value);
        }
        public int ModeIndex
        {
            get => (int)GetValue(ModeIndexProperty);
            set => SetValue(ModeIndexProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var combo = (ComboBox)GetTemplateChild("combo");

            if (combo is null) return;
            combo.SelectionChanged += (s, e) => ModeIndex = ((ComboBox)s).SelectedIndex;
        }
    }
}