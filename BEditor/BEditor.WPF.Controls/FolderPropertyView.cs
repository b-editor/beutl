using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BEditor.WPF.Controls
{
    public class FolderPropertyView : BasePropertyView
    {
        public static readonly DependencyProperty FolderProperty = DependencyProperty.Register(nameof(Folder), typeof(string), typeof(FolderPropertyView));
        public static readonly DependencyProperty OpenFolderCommandProperty = DependencyProperty.Register(nameof(OpenFolderCommand), typeof(ICommand), typeof(FolderPropertyView));

        static FolderPropertyView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FolderPropertyView), new FrameworkPropertyMetadata(typeof(FolderPropertyView)));
        }

        public ICommand OpenFolderCommand
        {
            get => (ICommand)GetValue(OpenFolderCommandProperty);
            set => SetValue(OpenFolderCommandProperty, value);
        }
        public string Folder
        {
            get => (string)GetValue(FolderProperty);
            set => SetValue(FolderProperty, value);
        }
    }
}
