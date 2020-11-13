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

namespace BEditor.Views.SettingsControl
{
    /// <summary>
    /// AppInfo.xaml の相互作用ロジック
    /// </summary>
    public partial class AppInfo : UserControl
    {
        public AppInfo()
        {
            InitializeComponent();

            version.DataContext = new AppInfoViewModel();
        }

        internal class AppInfoViewModel
        {
            public string AppVersion => "B Editor 0.0.3";
            public string OpenCVVersion => "OpenCvSharp 4.5.0.20201013";
            public string OpenGLVersion => "OpenTK 3.2.1";
        }
    }
}
