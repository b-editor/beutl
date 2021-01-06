using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BEditor.WPF.Controls
{
    public class FilePropertyView : BasePropertyView
    {
        public static readonly DependencyProperty FileProperty = DependencyProperty.Register(nameof(File), typeof(string), typeof(FilePropertyView));
        public static readonly DependencyProperty OpenFileCommandProperty = DependencyProperty.Register(nameof(OpenFileCommand), typeof(ICommand), typeof(FilePropertyView));

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
    }
}
